"""Docker effects are confined to the installed MonKado Compose project and local probes."""

from datetime import datetime, timezone
import io
import json
import os
import re
import shutil
import stat
import tarfile
from pathlib import Path
from time import monotonic, sleep

from deploy_policy import DeploymentError, dotenv, ensure
from deploy_process import command
from deploy_storage import atomic_write, private_read
from release_catalog import validate_catalog
from smoke_functional import FunctionalSmoke, credentials
from smoke_http import Client
import smoke_technical

ROOT = Path("/opt/monkado")
STATE = Path("/var/lib/monkado-deployment")
SETTINGS = Path("/etc/monkado")


def timestamp(value):
    """Parse Docker and application UTC timestamps without accepting naive dates."""
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    ensure(parsed.tzinfo is not None, "INVALID_TELEMETRY_TIME")
    return parsed


class Runtime:
    """Use only reviewed local commands; manifests select images, never executable scripts."""

    def __init__(self, runner=command, client_factory=Client, clock=monotonic, pause=sleep,
                 root=ROOT, state=STATE, settings=SETTINGS, observability=Path("/var/lib/monkado-observability")):
        self.run = runner
        self.clients = client_factory
        self.clock = clock
        self.pause = pause
        self.root = root
        self.state = state
        self.settings = settings
        self.observability = observability
        self.frontend_marker = None
        self.infrastructure = {}

    def observe_baseline(self):
        """Capture infrastructure and frontend identity before any application replacement."""
        self.infrastructure = {}
        for service in ("postgres", "caddy"):
            current = self.container(service)
            ensure(current["running"] is True and current["oom"] is False, "INFRASTRUCTURE_NOT_READY")
            self.infrastructure[service] = {key: current[key] for key in ("id", "image", "restarts")}
        options = json.loads(private_read(self.settings / "monitoring.json"))
        self.frontend_marker = None
        if options.get("frontendEnabled") is True:
            self.frontend_marker = smoke_technical.frontend(self.clients())
        return {"infrastructure": self.infrastructure, "frontend": self.frontend_marker}

    def restore_baseline(self, baseline):
        """Reuse the durable pre-maintenance baseline after an operator starts recovery."""
        ensure(isinstance(baseline, dict) and set(baseline) == {"infrastructure", "frontend"}, "BASELINE_REQUIRED")
        ensure(isinstance(baseline["infrastructure"], dict) and set(baseline["infrastructure"]) == {"caddy", "postgres"},
               "BASELINE_REQUIRED")
        self.infrastructure = baseline["infrastructure"]
        self.frontend_marker = baseline["frontend"]

    def compose(self, release, *arguments, timeout=120):
        """Supply image references as a private file, keeping secret values out of arguments."""
        path = self.state / "attempt-images.env"
        atomic_write(path, dotenv(release).encode())
        return self.run(["docker", "compose", "--project-name", "mon-kado", "--project-directory", str(self.root),
                         "--env-file", str(self.settings / "production.env"), "--env-file", str(path),
                         "-f", str(self.root / "compose.yaml"), "-f", str(self.root / "deployments/production/compose.production.yaml"),
                         *arguments], timeout=timeout)

    def inspect(self, name, template):
        """Read a whitelisted field rather than a full container inspection with credentials."""
        return self.run(["docker", "inspect", "--format", template, name]).decode().strip()

    def container(self, service):
        """Observe the installed container without reading environment variables."""
        return json.loads(self.inspect("mon-kado-" + service + "-1",
            '{"id":{{json .Id}},"image":{{json .Image}},"running":{{json .State.Running}},'
            '"oom":{{json .State.OOMKilled}},"restarts":{{json .RestartCount}},"startedAt":{{json .State.StartedAt}}}'))

    def verify_active(self, release):
        """Confirm both applications use the trusted release before relying on it for recovery."""
        for service in ("api", "worker"):
            current = self.container(service)
            image = self.inspect(release[service + "Image"], "{{.Id}}")
            ensure(current["running"] is True and current["image"] == image, "ACTIVE_RELEASE_MISMATCH")

    def catalog(self, release):
        """Read embedded metadata from an unstarted image container, never extract its archive."""
        container = self.run(["docker", "create", "--network", "none", "--read-only", "--entrypoint", "/bin/false",
                              release["apiImage"]]).decode().strip()
        ensure(re.fullmatch(r"[0-9a-f]{64}", container) is not None, "INVALID_CONTAINER_ID")
        try:
            raw = self.run(["docker", "cp", container + ":/app/migration-catalog.json", "-"], maximum=65536)
            with tarfile.open(fileobj=io.BytesIO(raw)) as archive:
                members = archive.getmembers()
                ensure(len(members) == 1 and members[0].isfile() and members[0].size <= 32768, "INVALID_IMAGE_CATALOG")
                with archive.extractfile(members[0]) as stream:
                    catalog = validate_catalog(json.load(stream))
            ensure(catalog == release["migrationCatalog"], "IMAGE_CATALOG_MISMATCH")
        except tarfile.TarError:
            raise DeploymentError("INVALID_IMAGE_CATALOG") from None
        finally:
            self.run(["docker", "rm", "-v", container])

    def preflight(self, candidate, current):
        """Complete every non-disruptive check before writing the maintenance intent."""
        private_read(self.settings / "production.env")
        account = credentials(self.settings / "deployment-smoke.json")
        ensure(shutil.disk_usage(self.state).free >= 2 * 1024 ** 3, "INSUFFICIENT_DISK")
        ensure(not (Path("/var/lib/monkado-backup") / "maintenance.json").exists(), "BACKUP_RECOVERY_REQUIRED")
        self.run(["systemctl", "is-active", "--quiet", "monkado.slice"])
        backup_check = ("import sys; sys.path[:0]=['/opt/monkado-backup','/opt/monkado/deployments/production']; "
                        "from monkado_backup.policy import FILES as backup; from release_manifest import FILES; "
                        "print(set(FILES).issubset(backup))")
        ensure(self.run(["python3", "-c", backup_check]) == b"True\n", "BACKUP_UPGRADE_REQUIRED")
        self.compose(candidate, "config", "--quiet")
        baseline = self.observe_baseline()
        for service in ("api", "worker"):
            self.run(["docker", "pull", candidate[service + "Image"]], timeout=300)
            revision = self.inspect(candidate[service + "Image"], '{{index .Config.Labels "org.opencontainers.image.revision"}}')
            ensure(revision == candidate["revision"], "IMAGE_REVISION_MISMATCH")
        self.catalog(candidate)
        if current is not None:
            for service in ("api", "worker"):
                self.inspect(current[service + "Image"], "{{.Id}}")
        ensure(shutil.disk_usage(self.state).free >= 1024 ** 3, "INSUFFICIENT_DISK")
        client = self.clients()
        probe = FunctionalSmoke(client, account, self.state / "smoke-journal.json")
        try:
            probe.login()
            if probe.journal.exists():
                probe.cleanup()
        finally:
            try:
                if client.token is not None:
                    client.json("DELETE", "/api/v1/auth/sessions/current", expected=204)
            finally:
                client.clear()
        return baseline

    def history(self):
        """Read only EF migration identifiers through PostgreSQL's local Unix socket."""
        sql = 'SELECT migration_id FROM public."__EFMigrationsHistory" ORDER BY migration_id;'
        raw = self.run(["docker", "exec", "mon-kado-postgres-1", "sh", "-c",
                        'exec psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -v ON_ERROR_STOP=1 -c "$1"',
                        "monkado-history", sql]).decode()
        result = raw.splitlines()
        ensure(len(result) <= 200 and all(re.fullmatch(r"[0-9]{14}_[A-Za-z0-9_]+", item) for item in result)
               and result == sorted(set(result)), "DATABASE_HISTORY_INVALID")
        return result

    def stop(self):
        """Stop only this project's application and proxy containers without removing volumes."""
        self.run(["docker", "stop", "--time", "30", "mon-kado-caddy-1", "mon-kado-api-1", "mon-kado-worker-1"], timeout=120)

    def migrate(self, release):
        """Execute a bundle once; a failure leaves migration recovery to the operator."""
        self.compose(release, "run", "--rm", "--no-deps", "migrations", timeout=180)

    def start(self, release):
        """Replace API and Worker only; keep infrastructure image IDs and persistent volumes."""
        self.compose(release, "up", "-d", "--no-deps", "--no-build", "--force-recreate", "api", "worker")
        self.run(["docker", "start", "mon-kado-caddy-1"])

    def telemetry(self, service, started_at):
        """Require a new snapshot from the new process, not a file left by the previous release."""
        path = self.observability / service / "snapshot.json"
        descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        with os.fdopen(descriptor, "rb") as stream:
            metadata = os.fstat(stream.fileno())
            ensure(stat.S_ISREG(metadata.st_mode) and metadata.st_uid == 1654 and
                   stat.S_IMODE(metadata.st_mode) == 0o600, "TELEMETRY_INVALID")
            raw = stream.read(65537)
        ensure(len(raw) <= 65536, "TELEMETRY_INVALID")
        value = json.loads(raw)
        created = timestamp(value["createdAt"])
        now = datetime.now(timezone.utc)
        ensure(value.get("schemaVersion") == 1 and value.get("service") == service and
               re.fullmatch(r"[0-9a-f]{32}", value.get("bootId", "")) is not None and
               created >= timestamp(started_at) and 0 <= (now - created).total_seconds() <= 60, "TELEMETRY_STALE")
        return value["bootId"]

    def technical_sample(self, release, deadline=None):
        """Fail immediately for OOM or process restarts; return readiness only with fresh evidence."""
        self.verify_active(release)
        for service in ("api", "worker", "caddy", "postgres"):
            current = self.container(service)
            ensure(current["running"] is True and current["oom"] is False, "CONTAINER_UNSTABLE")
            if service in ("api", "worker"):
                ensure(current["restarts"] == 0, "CONTAINER_UNSTABLE")
                self.telemetry(service, current["startedAt"])
            elif service in self.infrastructure:
                ensure({key: current[key] for key in ("id", "image", "restarts")} == self.infrastructure[service],
                       "INFRASTRUCTURE_CHANGED")
        smoke_technical.redirect()
        client = self.clients()
        if deadline is not None:
            client.deadline = min(client.deadline, deadline)
        smoke_technical.check(client)
        if self.frontend_marker is not None:
            ensure(smoke_technical.frontend(client) == self.frontend_marker, "FRONTEND_CHANGED")

    def smoke(self, release):
        """Wait for bounded readiness, then run the real authenticated persistence journey once."""
        deadline = self.clock() + 180
        healthy = 0
        while self.clock() < deadline:
            try:
                self.technical_sample(release, deadline)
                healthy += 1
                if healthy == 3:
                    break
            except DeploymentError as error:
                if error.code in ("CONTAINER_UNSTABLE", "INFRASTRUCTURE_CHANGED"):
                    raise
                healthy = 0
            except (OSError, ValueError, KeyError, TypeError):
                healthy = 0
            self.pause(5)
        ensure(healthy == 3, "TECHNICAL_SMOKE_TIMEOUT")
        FunctionalSmoke(self.clients(), credentials(self.settings / "deployment-smoke.json"),
                        self.state / "smoke-journal.json").run()
        self.technical_sample(release)
        return {"technical": True, "functional": True, "frontend": self.frontend_marker is not None}
