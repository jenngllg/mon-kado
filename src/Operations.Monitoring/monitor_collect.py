"""Read-only, bounded host observations. Never inspect environment variables or execute repairs."""

from datetime import datetime, timedelta, timezone
import http.client
import json
from pathlib import Path
import re
import ssl
import subprocess
import monitor_deployment
from zoneinfo import ZoneInfo

from monitor_policy import SERVICES, age, require, timestamp, validate_snapshot
from monitor_storage import read_json

UTC = timezone.utc
HOSTS = {"api": "api.monkado.fr", "frontend": "www.monkado.fr"}


def command(arguments):
    """Return a bounded technical subprocess result; do not propagate diagnostic text."""
    result = subprocess.run(arguments, capture_output=True, timeout=2, check=False)
    require(len(result.stdout) <= 65536)
    return result.returncode, result.stdout.decode("utf-8")


def https(service, path):
    """Verify the public certificate and never follow redirects or log response bodies."""
    require(service in HOSTS and path in ("/liveness", "/readiness", "/release.json", "/"))
    context = ssl.create_default_context()
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.verify_mode = ssl.CERT_REQUIRED
    context.check_hostname = True
    connection = http.client.HTTPSConnection(HOSTS[service], timeout=5, context=context)
    try:
        connection.connect()
        certificate = connection.sock.getpeercert()
        expires = datetime.fromtimestamp(ssl.cert_time_to_seconds(certificate["notAfter"]), UTC).isoformat()
        connection.request("GET", path, headers={"User-Agent": "MonKado-local-monitoring"})
        response = connection.getresponse()
        body = response.read(65537)
        require(len(body) <= 65536)
        healthy = response.status == 200
        if service == "frontend" and path == "/release.json" and healthy:
            marker = json.loads(body)
            healthy = (isinstance(marker, dict) and re.fullmatch(r"[0-9a-f]{40}", str(marker.get("revision", ""))) is not None
                       and marker.get("apiOrigin") == "https://api.monkado.fr" and type(marker.get("googleEnabled")) is bool)
        return not healthy, expires
    finally:
        connection.close()


class Collector:
    """Observe known installation paths with substitutable network and process dependencies."""

    def __init__(self, root=Path("/var/lib"), cgroup=Path("/sys/fs/cgroup/monkado.slice/memory.events"), runner=command, probe=https):
        self.root = root
        self.cgroup = cgroup
        self.runner = runner
        self.probe = probe

    def backup(self, now):
        """Adapt MK-813's retained status without loading its unmerged code or accessing Drive."""
        value = read_json(self.root / "monkado-backup/status.json")
        require(isinstance(value, dict))
        selected = {key: value.get(key) for key in ("lastRemoteCapture", "lastIntegrityCheck")}
        for item in selected.values():
            if item is not None:
                age(item, now)
        active, _ = self.runner(["systemctl", "is-active", "--quiet", "monkado-backup.service", "monkado-backup-transfer.service"])
        selected["terminalFailure"] = bool(value.get("error")) and active != 0
        local = now.astimezone(ZoneInfo("Europe/Paris"))
        expected = local.replace(hour=3, minute=0, second=0, microsecond=0)
        if local < expected + timedelta(minutes=15):
            expected -= timedelta(days=1)
        captured = value.get("lastCapture")
        selected["missedScheduledCapture"] = captured is None or timestamp(captured) < expected
        return selected

    def containers(self, now, previous, window_seconds):
        """Inspect only running state, OOM evidence and restart counters for fixed containers."""
        samples = []
        observations = {}
        for service in SERVICES:
            try:
                code, output = self.runner(["docker", "inspect", "--format",
                                             '{"id":{{json .Id}},"running":{{json .State.Running}},"oom":{{json .State.OOMKilled}},"restarts":{{json .RestartCount}}}',
                                             "mon-kado-" + service + "-1"])
                require(code == 0)
                current = json.loads(output)
                require(isinstance(current, dict) and re.fullmatch(r"[0-9a-f]{64}", current.get("id", "")) is not None)
                require(type(current.get("running")) is bool and type(current.get("oom")) is bool)
                require(type(current.get("restarts")) is int and current["restarts"] >= 0)
                earlier = [item for item in previous if item["service"] == service and item["id"] == current["id"]
                           and age(item["createdAt"], now) <= window_seconds]
                baseline = min(earlier, key=lambda item: timestamp(item["createdAt"]), default=current)
                latest = max(earlier, key=lambda item: timestamp(item["createdAt"]), default={"oom": False})
                observations[service] = {"running": current["running"], "newOom": current["oom"] and not latest["oom"],
                                         "recentRestarts": max(0, current["restarts"] - baseline["restarts"])}
                samples.append({"service": service, "createdAt": now.isoformat(), "id": current["id"],
                                "oom": current["oom"], "restarts": current["restarts"]})
            except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
                observations[service] = None
        return observations, samples

    def collect(self, now, previous, options, frontend_enabled):
        """Missing or invalid evidence remains unknown and cannot silently close an incident."""
        result = {"frontendEnabled": frontend_enabled, "checks": {}, "certificates": {}, "snapshots": {}, "maintenance": [], "frontendMaintenance": []}
        for service, path, check in (("api", "/liveness", "api"), ("api", "/readiness", "database"),
                                      ("frontend", "/", "frontend"), ("frontend", "/release.json", "frontend")):
            if service == "frontend" and not frontend_enabled:
                continue
            try:
                bad, expires = self.probe(service, path)
                result["checks"][check] = result["checks"].get(check, False) or bad
                result["certificates"][service] = expires
            except (OSError, ValueError, TypeError, KeyError, http.client.HTTPException):
                result["checks"][check] = True
                result["certificates"].setdefault(service, None)
        for service in ("api", "worker"):
            try:
                result["snapshots"][service] = validate_snapshot(read_json(self.root / ("monkado-observability/" + service + "/snapshot.json")), service)
                age(result["snapshots"][service]["createdAt"], now)
            except (OSError, ValueError, TypeError, KeyError):
                result["snapshots"][service] = None
        result["containers"], samples = self.containers(now, previous.get("containerHistory", []), options["restartWindowSeconds"])
        try:
            raw = self.cgroup.read_text(encoding="ascii")
            require(len(raw) <= 4096)
            oom = int({key: value for key, value in (line.split() for line in raw.splitlines())}["oom_kill"])
            require(oom >= 0)
            result["newHostOom"] = previous.get("oomKills") is not None and oom > previous["oomKills"]
        except (OSError, ValueError, KeyError):
            oom = None
            result["newHostOom"] = None
        for marker, destination in (("monkado-backup/maintenance.json", "maintenance"),
                                    ("monkado-deployment/in-progress.env", "maintenance"),
                                    ("monkado-frontend/transition.json", "frontendMaintenance")):
            path = self.root / marker
            if path.exists():
                result[destination].append(datetime.fromtimestamp(path.stat().st_mtime, UTC).isoformat())
        try:
            result["backup"] = self.backup(now)
        except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
            result["backup"] = None
        try:
            result["deployment"] = monitor_deployment.collect(self.root, self.runner, now)
        except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
            result["deployment"] = None
        if frontend_enabled:
            try:
                result["frontendDeployment"] = monitor_deployment.collect_frontend(self.root, self.runner, now)
            except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
                result["frontendDeployment"] = None
        return result, samples, oom
