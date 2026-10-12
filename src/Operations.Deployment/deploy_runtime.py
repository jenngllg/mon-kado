"""Native effects are confined to approved binaries, fixed systemd units and local probes."""

from datetime import datetime, timezone
import json
import os
import re
import shutil
import stat
import sys
from pathlib import Path
from time import monotonic, sleep

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Operations.Native"))
import native_artifacts
import native_services
from native_configuration import validate_database_environment

from deploy_policy import DeploymentError, dotenv, ensure
from deploy_process import command
from deploy_storage import atomic_write, private_read
from release_catalog import NATIVE_MIGRATION
from smoke_functional import FunctionalSmoke, credentials
from smoke_http import Client
import smoke_technical

ROOT = Path("/opt/monkado")
STATE = Path("/var/lib/monkado-deployment")
SETTINGS = Path("/etc/monkado")


def timestamp(value):
    """Parse native process and application UTC timestamps without accepting naive dates."""
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    ensure(parsed.tzinfo is not None, "INVALID_TELEMETRY_TIME")
    return parsed


class Runtime:
    """Use reviewed commands and approved immutable binaries, never downloaded operational scripts."""

    def __init__(self, runner=command, client_factory=Client, clock=monotonic, pause=sleep,
                 root=ROOT, state=STATE, settings=SETTINGS, observability=Path("/var/lib/monkado-observability"),
                 transport=native_artifacts.download, observer=native_services.observe, service_uid=None):
        self.run = runner
        self.clients = client_factory
        self.clock = clock
        self.pause = pause
        self.root = root
        self.state = state
        self.settings = settings
        self.observability = observability
        self.transport = transport
        self.observer = observer
        self.service_uid = service_uid
        self.frontend_marker = None
        self.infrastructure = {}

    def observe_baseline(self):
        """Capture infrastructure and frontend identity before any application replacement."""
        self.infrastructure = {}
        for service in ("postgres", "caddy"):
            current = self.container(service)
            ensure(current["running"] is True and current["oom"] is False, "INFRASTRUCTURE_NOT_READY")
            self.infrastructure[service] = self.infrastructure_identity(service, current)
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

    def container(self, service):
        """Preserve the observation contract while reading only native service properties."""
        return self.observer(service, self.run)

    @staticmethod
    def infrastructure_identity(service, current):
        """Allow the planned Caddy stop/start while detecting executable, host or restart changes."""
        return {"id": current["monitorId"] if service == "caddy" else current["id"],
                "image": current["image"], "restarts": current["restarts"]}

    def directory(self, release):
        """Resolve a verified immutable publication beneath the fixed installation root."""
        return self.root / "releases" / native_artifacts.identity(release)

    def prepare(self, release):
        """Materialize and inspect trusted executables before entering maintenance."""
        ensure(shutil.disk_usage(self.state).free >= 2 * 1024 ** 3, "INSUFFICIENT_DISK")
        directory = native_artifacts.materialize(self.root / "releases", release, self.transport)
        native_artifacts.inspect(directory, release, self.run)
        return directory

    def verify_active(self, release):
        """Confirm both running executable digests match the trusted publication."""
        ensure(release.get("schemaVersion") == 3, "NATIVE_PUBLICATION_REQUIRED")
        for service in ("api", "worker"):
            current = self.container(service)
            ensure(current["running"] is True and current["image"] == release["binaries"][service]["sha256"], "ACTIVE_RELEASE_MISMATCH")

    def catalog(self, release):
        """Verify the embedded SQL catalog only after checking the approved binary digests."""
        native_artifacts.inspect(self.directory(release), release, self.run)

    def preflight(self, candidate, current):
        """Complete every non-disruptive check before writing the maintenance intent."""
        validate_database_environment(private_read(self.settings / "production.env"))
        account = credentials(self.settings / "deployment-smoke.json")
        ensure(shutil.disk_usage(self.state).free >= 2 * 1024 ** 3, "INSUFFICIENT_DISK")
        ensure(not (Path("/var/lib/monkado-backup") / "maintenance.json").exists(), "BACKUP_RECOVERY_REQUIRED")
        self.run(["systemctl", "is-active", "--quiet", "monkado.slice"])
        backup_check = ("import sys; sys.path[:0]=['/opt/monkado-backup','/opt/monkado/deployments/production']; "
                        "from monkado_backup.policy import FILES as backup; from release_manifest import FILES; "
                        "print(set(FILES).issubset(backup))")
        ensure(self.run(["python3", "-c", backup_check]) == b"True\n", "BACKUP_UPGRADE_REQUIRED")
        baseline = self.observe_baseline()
        self.prepare(candidate)
        if current is not None:
            native_artifacts.verify_directory(self.directory(current), current)
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
        """Read only native migration identities through PostgreSQL's local peer-authenticated socket."""
        prefix = ["runuser", "-u", "postgres", "--", "psql", "-X", "--no-password", "-d", "mon_kado", "-At", "-v", "ON_ERROR_STOP=1", "-c"]
        exists = self.run([*prefix, "SELECT COALESCE(to_regclass('public.mon_kado_migrations')::text,'');"], maximum=1024).decode().strip()
        ensure(exists in ("", "mon_kado_migrations"), "DATABASE_HISTORY_INVALID")
        if not exists:
            return []
        raw = self.run([*prefix, "SELECT name FROM public.mon_kado_migrations ORDER BY name;"], maximum=32768).decode()
        result = raw.splitlines()
        ensure(len(result) <= 200 and all(re.fullmatch(NATIVE_MIGRATION, item) for item in result)
               and result == sorted(set(result)), "DATABASE_HISTORY_INVALID")
        return result

    def empty_database(self):
        """Refuse first installation over any existing application table, including legacy schemas."""
        raw = self.run(["runuser", "-u", "postgres", "--", "psql", "-X", "--no-password", "-d", "mon_kado", "-At",
                        "-v", "ON_ERROR_STOP=1", "-c",
                        "SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema');"], maximum=1024)
        ensure(re.fullmatch(rb"[0-9]{1,10}\n?", raw) is not None, "DATABASE_HISTORY_INVALID")
        return raw.strip() == b"0"

    def stop(self):
        """Stop the application and proxy units without touching PostgreSQL or durable files."""
        self.run(["systemctl", "stop", *[native_services.unit(service) for service in ("caddy", "api", "worker")]], timeout=120)

    def migrate(self, release):
        """Execute approved SQL once with a private EnvironmentFile; never expose a database URL in argv."""
        directory = self.directory(release)
        native_artifacts.verify_directory(directory, release)
        self.run(["systemd-run", "--quiet", "--wait", "--pipe", "--collect",
                  "--unit=mon-kado-migrate-" + release["publicationId"], "--property=User=monkado", "--property=Group=monkado",
                  "--property=EnvironmentFile=" + str(self.settings / "production.env"), "--property=UMask=0077",
                  "--setenv=APP_VERSION=" + release["revision"], "--property=Slice=monkado.slice",
                  "--setenv=GOMEMLIMIT=256MiB",
                  "--property=NoNewPrivileges=yes", "--property=ProtectSystem=strict", "--property=ProtectHome=yes",
                  "--", str(directory / "migrate")], timeout=180)

    def start(self, release):
        """Replace the application pointer atomically, retaining prior verified executables for rollback."""
        directory = self.directory(release)
        native_artifacts.verify_directory(directory, release)
        current = self.root / "current"
        ensure(not current.exists() or current.is_symlink(), "UNSAFE_RELEASE_POINTER")
        temporary = self.root / (".current-" + release["publicationId"])
        ensure(not temporary.exists() and not temporary.is_symlink(), "UNSAFE_RELEASE_POINTER")
        try:
            temporary.symlink_to(directory.relative_to(self.root), target_is_directory=True)
            atomic_write(self.state / "active.env", dotenv(release).encode())
            temporary.replace(current)
            native_artifacts.sync_directory(self.root)
        finally:
            temporary.unlink(missing_ok=True)
        # Clear old restart counters only after replacement, before recording the new process baseline.
        self.run(["systemctl", "reset-failed", native_services.unit("api"), native_services.unit("worker")])
        self.run(["systemctl", "start", *[native_services.unit(service) for service in ("api", "worker", "caddy")]], timeout=120)

    def telemetry(self, service, started_at):
        """Require a new snapshot from the new process, not a file left by the previous release."""
        if self.service_uid is None:
            import pwd
            self.service_uid = pwd.getpwnam("monkado").pw_uid
        path = self.observability / service / "snapshot.json"
        descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        with os.fdopen(descriptor, "rb") as stream:
            metadata = os.fstat(stream.fileno())
            ensure(stat.S_ISREG(metadata.st_mode) and metadata.st_uid == self.service_uid and
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
        for service in ("api", "worker", "caddy", "postgres"):
            current = self.container(service)
            ensure(current["running"] is True and current["oom"] is False, "SERVICE_UNSTABLE")
            if service in ("api", "worker"):
                ensure(current["restarts"] == 0, "SERVICE_UNSTABLE")
                self.telemetry(service, current["startedAt"])
            elif service in self.infrastructure:
                ensure(self.infrastructure_identity(service, current) == self.infrastructure[service],
                       "INFRASTRUCTURE_CHANGED")
        # A dead process has no executable digest. Detect its terminal failure before
        # identity checking, so OOM/exit failures cannot become a readiness timeout.
        self.verify_active(release)
        smoke_technical.redirect()
        client = self.clients()
        if deadline is not None:
            client.deadline = min(client.deadline, deadline)
        smoke_technical.check(client)
        if self.frontend_marker is not None:
            ensure(smoke_technical.frontend(client) == self.frontend_marker, "FRONTEND_CHANGED")

    def wait_technical(self, release):
        """Require three bounded consecutive readiness samples without running a member journey."""
        deadline = self.clock() + 180
        healthy = 0
        while self.clock() < deadline:
            try:
                self.technical_sample(release, deadline)
                healthy += 1
                if healthy == 3:
                    break
            except DeploymentError as error:
                if error.code in ("SERVICE_UNSTABLE", "INFRASTRUCTURE_CHANGED"):
                    raise
                healthy = 0
            except (OSError, ValueError, KeyError, TypeError, AttributeError):
                healthy = 0
            self.pause(5)
        ensure(healthy == 3, "TECHNICAL_SMOKE_TIMEOUT")

    def smoke(self, release):
        """Wait for bounded readiness, then run the real authenticated persistence journey once."""
        self.wait_technical(release)
        try:
            FunctionalSmoke(self.clients(), credentials(self.settings / "deployment-smoke.json"),
                            self.state / "smoke-journal.json").run()
            self.technical_sample(release)
        except (OSError, ValueError, KeyError, TypeError, AttributeError):
            raise DeploymentError("SMOKE_CONTRACT_FAILED") from None
        return {"technical": True, "functional": True, "frontend": self.frontend_marker is not None}
