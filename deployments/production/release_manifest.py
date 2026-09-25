"""Validate the public release contract without executing downloaded content."""

import hashlib
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from release_catalog import fingerprint, validate_catalog

FILES = (
    "compose.yaml",
    "deployments/caddy/Caddyfile",
    "deployments/production/compose.production.yaml",
    "deployments/production/monkado.slice",
    "deployments/production/deploy.sh",
    "deployments/production/release_manifest.py",
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
REPOSITORY = "jenngllg/mon-kado"


def configuration_hash(root):
    """Bind a release to the reviewed deployment files installed on the VPS."""
    digest = hashlib.sha256()
    for name in FILES:
        digest.update(name.encode() + b"\0")
        digest.update((root / name).read_bytes().replace(b"\r\n", b"\n"))
        digest.update(b"\0")
    return digest.hexdigest()


def validate(value, expected_hash):
    """Reject unknown fields, mutable tags, other repositories and unsafe values."""
    fields = {"schemaVersion", "revision", "configurationHash", "apiImage", "workerImage"}
    if isinstance(value, dict) and value.get("schemaVersion") == 2:
        fields.update({"publicationId", "migrationCatalog", "migrationHash", "rollbackAllowed"})
    if not isinstance(value, dict) or set(value) != fields:
        raise ValueError("Unexpected manifest fields")
    if type(value["schemaVersion"]) is not int or value["schemaVersion"] not in (1, 2):
        raise ValueError("Unsupported manifest version")
    if not isinstance(value["revision"], str) or not re.fullmatch(r"[0-9a-f]{40}", value["revision"]):
        raise ValueError("Invalid revision")
    if value["configurationHash"] != expected_hash:
        raise ValueError("Deployment configuration changed: install reviewed files before deploying")
    for key, component in (("apiImage", "api"), ("workerImage", "worker")):
        pattern = rf"ghcr\.io/jenngllg/mon-kado-{component}@sha256:[0-9a-f]{{64}}"
        if not isinstance(value[key], str) or not re.fullmatch(pattern, value[key]):
            raise ValueError("Invalid immutable image reference")
    if value["schemaVersion"] == 2:
        validate_v2(value)
    return value


def validate_v2(value):
    """Bind a unique approval to a complete catalog and an explicit rollback declaration."""
    if not isinstance(value["publicationId"], str) or not re.fullmatch(r"[1-9][0-9]{0,19}-[1-9][0-9]{0,5}", value["publicationId"]):
        raise ValueError("Invalid publication identity")
    if type(value["rollbackAllowed"]) is not bool:
        raise ValueError("Invalid rollback declaration")
    catalog = validate_catalog(value["migrationCatalog"])
    if value["migrationHash"] != fingerprint(catalog):
        raise ValueError("Migration catalog does not match its fingerprint")


def from_release(release, expected_hash):
    """Read only the explicit production release, never an arbitrary latest release."""
    if not isinstance(release, dict):
        raise ValueError("Invalid release")
    if release.get("tag_name") != "backend-production" or release.get("draft") is not False or release.get("prerelease") is not False:
        raise ValueError("Not a published production release")
    body = release.get("body")
    if not isinstance(body, str) or len(body) > 32768:
        raise ValueError("Invalid release body")
    return validate(json.loads(body), expected_hash)


def main(arguments):
    """Print either the local contract hash or strictly validated dotenv values."""
    root = Path(__file__).resolve().parents[2]
    expected_hash = configuration_hash(root)
    if arguments == ["hash"]:
        print(expected_hash)
    elif len(arguments) == 2 and arguments[0] == "release":
        data = Path(arguments[1]).read_bytes()
        if len(data) > 65536:
            raise ValueError("Release response too large")
        value = from_release(json.loads(data), expected_hash)
        print(f'API_IMAGE={value["apiImage"]}')
        print(f'WORKER_IMAGE={value["workerImage"]}')
        print(f'RELEASE_REVISION={value["revision"]}')
    else:
        raise ValueError("Usage: release_manifest.py hash | release response.json")


if __name__ == "__main__":
    try:
        main(sys.argv[1:])
    except (ValueError, OSError, TypeError):
        print("Invalid release or local configuration; deployment refused.", file=sys.stderr)
        sys.exit(1)
