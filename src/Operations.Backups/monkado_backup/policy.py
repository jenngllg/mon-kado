"""Pure validation and retention rules for backup trust boundaries."""

import re
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo

UTC = timezone.utc
TAG = "monkado-production-v1"
HASH = r"[0-9a-f]{64}"
LEGACY_FILES = (
    "compose.yaml",
    "deployments/caddy/Caddyfile",
    "deployments/production/compose.production.yaml",
    "deployments/production/deploy.sh",
    "deployments/production/release_manifest.py",
    "deployments/production/monkado.slice",
)
PREVIOUS_FILES = LEGACY_FILES + (
    "deployments/frontend/frontend.caddy",
    "deployments/frontend/monkado-frontend.service",
    "deployments/frontend/monkado-frontend.timer",
    "src/Operations.Frontend/frontend_contract.py",
    "src/Operations.Frontend/frontend_runtime.py",
    "src/Operations.Frontend/frontend_cli.py",
    "deployments/monitoring/monkado-monitor.service",
    "deployments/monitoring/monkado-monitor.timer",
    "deployments/monitoring/monitoring.json.example",
    "src/Operations.Monitoring/monitor_cli.py",
    "src/Operations.Monitoring/monitor_collect.py",
    "src/Operations.Monitoring/monitor_gmail.py",
    "src/Operations.Monitoring/monitor_policy.py",
    "src/Operations.Monitoring/monitor_provision.py",
    "src/Operations.Monitoring/monitor_runtime.py",
    "src/Operations.Monitoring/monitor_storage.py",
)
DEPLOYMENT_FILES = PREVIOUS_FILES + (
    "src/Operations.Monitoring/monitor_deployment.py",
    "src/Operations.Deployment/release_catalog.py",
    "src/Operations.Deployment/deploy_policy.py",
    "src/Operations.Deployment/deploy_storage.py",
    "src/Operations.Deployment/deploy_engine.py",
    "src/Operations.Deployment/deploy_cli.py",
    "src/Operations.Deployment/deploy_process.py",
    "src/Operations.Deployment/deploy_provision.py",
    "src/Operations.Deployment/deploy_runtime.py",
    "src/Operations.Deployment/smoke_functional.py",
    "src/Operations.Deployment/smoke_http.py",
    "src/Operations.Deployment/smoke_technical.py",
    "deployments/production/monkado-deploy",
    "deployments/production/monkado-deploy.service",
    "deployments/production/monkado-deploy.timer",
)
FILES = DEPLOYMENT_FILES + (
    "src/Operations.Frontend/frontend_state.py",
    "src/Operations.Frontend/frontend_probe.py",
)


class BackupError(Exception):
    """A bounded technical error code safe to expose in operational status."""

    def __init__(self, code):
        if not re.fullmatch(r"[A-Z_]{1,64}", code):
            code = "INVALID_ERROR_CODE"
        super().__init__(code)
        self.code = code


def timestamp(value):
    """Parse an explicit offset timestamp without silently accepting local time."""
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            raise ValueError()
        return parsed.astimezone(UTC)
    except (AttributeError, TypeError, ValueError):
        raise BackupError("INVALID_TIMESTAMP") from None


def image_reference(value, component):
    """Accept only the existing project's immutable API or Worker reference."""
    if not isinstance(value, str) or not re.fullmatch(
        rf"ghcr\.io/jenngllg/mon-kado-{component}@sha256:{HASH}", value
    ):
        raise BackupError("INVALID_IMAGE_REFERENCE")
    return value


def release_metadata(text):
    """Parse the deployed pointer as data, never as shell instructions."""
    try:
        values = {key: value for key, value in (line.split("=", 1) for line in text.splitlines())}
        if set(values) != {"API_IMAGE", "WORKER_IMAGE", "RELEASE_REVISION"}:
            raise ValueError()
        if not re.fullmatch(r"[0-9a-f]{40}", values["RELEASE_REVISION"]):
            raise ValueError()
        image_reference(values["API_IMAGE"], "api")
        image_reference(values["WORKER_IMAGE"], "worker")
        return values
    except (AttributeError, TypeError, ValueError):
        raise BackupError("INVALID_RELEASE_POINTER") from None


def snapshot_id(value):
    """Require an exact immutable snapshot, not latest or a partial identifier."""
    if not isinstance(value, str) or not re.fullmatch(HASH, value):
        raise BackupError("INVALID_SNAPSHOT_ID")
    return value


def expired_snapshots(snapshots, verified_id, now):
    """Use actual UTC age, not fourteen successful days or Restic's latest date."""
    snapshot_id(verified_id)
    owned = [item for item in snapshots if TAG in item.get("tags", [])]
    if not any(item.get("id") == verified_id for item in owned):
        raise BackupError("VERIFIED_SNAPSHOT_MISSING")
    cutoff = now - timedelta(days=14)
    return [
        snapshot_id(item.get("id"))
        for item in owned
        if item.get("id") != verified_id and timestamp(item.get("time")) < cutoff
    ]


def status_health(state, now):
    """Return a non-sensitive status; absence of evidence is not success."""
    last = state.get("lastRemoteCapture")
    if not last or now - timestamp(last) > timedelta(hours=30):
        return "degraded"
    if state.get("error"):
        return "degraded"
    return "healthy"


def missed_capture(state, now):
    """Report a missed Paris schedule without triggering daytime maintenance."""
    local = now.astimezone(ZoneInfo("Europe/Paris"))
    expected = local.replace(hour=3, minute=0, second=0, microsecond=0)
    if local < expected + timedelta(minutes=15):
        expected -= timedelta(days=1)
    last = state.get("lastCapture")
    return not last or timestamp(last) < expected.astimezone(UTC)
