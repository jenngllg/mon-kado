"""Linux host orchestration. Provider access is delegated to Restic/rclone."""

import contextlib
import fcntl
import json
import os
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from .capture import FILES, NATIVE_IMAGE_NAME, atomic_json, copy_files, copy_deployment_private, create_native_manifest, verify_manifest
from .policy import BackupError, TAG, expired_snapshots, release_metadata, snapshot_id, status_health, timestamp, missed_capture

source = Path(__file__).resolve().parents[2]
if not (source / "Operations.Native").is_dir():
    source = Path("/opt/monkado/src")
sys.path[:0] = [str(source / "Operations.Native"), str(source / "Operations.Deployment"),
               str(source.parent / "deployments/production")]
import native_artifacts
import native_services
from native_configuration import validate_database_environment
from release_manifest import configuration_hash, validate
from release_catalog import identifiers
from deploy_policy import dotenv, validate_state

ROOT = Path("/opt/monkado")
STATE = Path("/var/lib/monkado-backup")
SETTINGS = Path("/etc/monkado-backup")
DEPLOYMENT = Path("/var/lib/monkado-deployment")
PRODUCTION_ENV = Path("/etc/monkado/production.env")
SERVICES = ("api", "worker", "caddy")
CURRENT_ENV_NAME = "current.env"
RCLONE_CONFIG_NAME = "rclone.conf"
MAINTENANCE_NAME = "maintenance.json"
CANDIDATE_NAME = "capture.new"
PREVIOUS_NAME = "pending.previous"


def utcnow():
    """Return an aware UTC timestamp for operational state."""
    return datetime.now(timezone.utc)


def command(arguments, *, output=None, input_file=None, timeout=600, cwd=None, extra_env=None):
    """Never relay subprocess stderr, which can contain credentials or data."""
    environment = {"PATH": "/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin", "HOME": "/root", "LANG": "C.UTF-8", "TZ": "UTC"}
    environment.update(extra_env or {})
    try:
        result = subprocess.run(arguments, stdout=output or subprocess.PIPE, stdin=input_file or subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL, timeout=timeout,
                                check=False, env=environment, cwd=cwd)
    except subprocess.TimeoutExpired:
        raise BackupError("COMMAND_TIMEOUT") from None
    except OSError:
        raise BackupError("COMMAND_FAILED") from None
    if result.returncode:
        raise BackupError("COMMAND_FAILED")
    return result.stdout.decode("utf-8") if output is None else ""


@contextlib.contextmanager
def lock(path):
    """Use the same advisory lock protocol as the installed deployment service."""
    with path.open("a") as stream:
        try:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise BackupError("OPERATION_ALREADY_RUNNING") from None
        yield


def private_file(path):
    """Refuse missing, linked or non-private root credentials."""
    info = path.lstat()
    if path.is_symlink() or not path.is_file() or info.st_uid != 0 or info.st_mode & 0o777 != 0o600:
        raise BackupError("UNSAFE_CREDENTIAL_FILE")


class Operations:
    """Coordinate immutable capture, offsite transfer and safe crash recovery."""

    def __init__(self, run=command, now=utcnow, wait=time.sleep, observer=native_services.observe,
                 storage=Path("/var/lib/monkado")):
        self.run = run
        self.now = now
        self.wait = wait
        self.observer = observer
        self.storage = storage

    def state(self):
        """Read bounded operational metadata, never secret-bearing captures."""
        path = STATE / "status.json"
        return json.loads(path.read_text()) if path.exists() else {}

    def update(self, **changes):
        """Publish operational progress atomically."""
        value = self.state()
        value.update(changes)
        atomic_json(STATE / "status.json", value)

    def service(self, service):
        """Observe one fixed native unit without accessing its environment or raw logs."""
        try:
            return self.observer(service, self.run)
        except (OSError, ValueError, KeyError, TypeError):
            raise BackupError("SERVICE_OBSERVATION_FAILED") from None

    def running(self, service):
        """Require a live native service before entering a capture maintenance window."""
        current = self.service(service)
        if not current["running"] or current["oom"]:
            raise BackupError("SERVICE_NOT_RUNNING")
        return current

    def volume(self, name):
        """Use only the configured native durable image root, never caller-provided paths."""
        if name != "gift_images":
            raise BackupError("INVALID_STORAGE_PATH")
        path = self.storage / "gift-images"
        if not path.is_absolute() or not path.is_dir() or path.is_symlink():
            raise BackupError("INVALID_VOLUME_PATH")
        return path

    def database(self, sql):
        """Run reviewed fixed queries through PostgreSQL's local peer socket without password argv."""
        return self.run(["runuser", "-u", "postgres", "--", "psql", "-X", "--no-password", "-d", "mon_kado",
                         "-At", "-v", "ON_ERROR_STOP=1", "-c", sql], timeout=15).strip()

    def approved(self):
        """Verify retained approval, executable bytes and private pointers before stopping writers."""
        for path in (DEPLOYMENT / "status.json", DEPLOYMENT / CURRENT_ENV_NAME, DEPLOYMENT / "active.env"):
            private_file(path)
            if path.stat().st_size > 131072:
                raise BackupError("INVALID_RELEASE_POINTER")
        state = validate_state(json.loads((DEPLOYMENT / "status.json").read_bytes()))
        if state["phase"] not in ("succeeded", "acknowledged", "rolledBack", "rejected") or state["current"] is None:
            raise BackupError("DEPLOYMENT_INCOMPLETE")
        approved = validate(state["current"], configuration_hash(ROOT))
        if approved["schemaVersion"] != 3:
            raise BackupError("NATIVE_PUBLICATION_REQUIRED")
        pointer = dotenv(approved).encode()
        if any((DEPLOYMENT / name).read_bytes() != pointer for name in (CURRENT_ENV_NAME, "active.env")):
            raise BackupError("RELEASE_MISMATCH")
        directory = ROOT / "releases" / native_artifacts.identity(approved)
        native_artifacts.verify_directory(directory, approved)
        for service in ("api", "worker"):
            if self.running(service)["image"] != approved["binaries"][service]["sha256"]:
                raise BackupError("ACTIVE_RELEASE_MISMATCH")
        if self.database("SELECT name FROM public.mon_kado_migrations ORDER BY name;").splitlines() != identifiers(approved["migrationCatalog"]):
            raise BackupError("DATABASE_HISTORY_DIVERGED")
        return approved, directory

    def restic(self, *arguments, cwd=None):
        """Use a dedicated repository and credential files, without secret arguments."""
        for name in ("password", RCLONE_CONFIG_NAME, "repository"):
            private_file(SETTINGS / name)
        repository = (SETTINGS / "repository").read_text().strip()
        if repository != "rclone:monkado:MonKado-backups/production-v1":
            raise BackupError("UNEXPECTED_REPOSITORY")
        return self.run(["restic", "--repository-file", str(SETTINGS / "repository"),
                         "--password-file", str(SETTINGS / "password"), "--cache-dir", str(STATE / "cache"),
                         "-o", "rclone.args=serve restic --stdio --drive-use-trash=false", *arguments],
                        timeout=3600, cwd=cwd,
                        extra_env={"RCLONE_CONFIG": str(SETTINGS / RCLONE_CONFIG_NAME), "GOMEMLIMIT": "128MiB", "GOMAXPROCS": "2"})

    def recover(self):
        """Restart only services stopped by this tool, including after termination."""
        marker = STATE / MAINTENANCE_NAME
        if not marker.exists():
            return
        if json.loads(marker.read_text()) != {"services": list(SERVICES)}:
            raise BackupError("INVALID_MAINTENANCE_MARKER")
        for service in SERVICES:
            self.service(service)
        self.run(["systemctl", "start", *[native_services.unit(service) for service in SERVICES]], timeout=120)
        for _ in range(30):
            try:
                self.run(["curl", "--fail", "--silent", "--show-error", "--max-time", "5",
                          "--resolve", "api.monkado.fr:443:127.0.0.1", "https://api.monkado.fr/readiness"])
                self.running("worker")
                marker.unlink()
                return
            except BackupError:
                self.wait(2)
        raise BackupError("SERVICE_RECOVERY_FAILED")

    def capture(self):
        """Stop writers only while taking the consistent local copy, never during upload."""
        with lock(DEPLOYMENT / "backup-coordination.lock"), lock(DEPLOYMENT / "deploy.lock"):
            if (DEPLOYMENT / "in-progress.env").exists():
                raise BackupError("DEPLOYMENT_INCOMPLETE")
            self.recover()
            self.recover_capture()
            candidate = STATE / CANDIDATE_NAME
            # A completed capture may survive a failed restart or interrupted promotion.
            # Require its explicit transfer before another maintenance can replace it.
            if (candidate / "manifest.json").exists():
                raise BackupError("INTERRUPTED_CAPTURE_REQUIRES_TRANSFER")
            private_file(PRODUCTION_ENV)
            validate_database_environment(PRODUCTION_ENV.read_bytes())
            caddy_environment = PRODUCTION_ENV.parent / "caddy.env"
            private_file(caddy_environment)
            approved, directory = self.approved()
            for service in SERVICES:
                self.running(service)
            postgres = self.running("postgres")
            caddy_digest = self.running("caddy")["image"]
            images = self.volume("gift_images")
            size = int(self.database("SELECT pg_database_size(current_database());"))
            size += sum(p.stat().st_size for p in images.rglob("*") if p.is_file())
            size += sum(artifact["size"] for artifact in approved["binaries"].values())
            if shutil.disk_usage(STATE).free < size * 2 + 512 * 1024 * 1024:
                raise BackupError("INSUFFICIENT_LOCAL_SPACE")
            if candidate.exists():
                self.remove_capture(candidate)
            candidate.mkdir(mode=0o700)
            atomic_json(STATE / MAINTENANCE_NAME, {"services": list(SERVICES)})
            try:
                self.run(["systemctl", "stop", *[native_services.unit(service) for service in ("caddy", "api", "worker")]], timeout=120)
                for service in SERVICES:
                    if self.service(service)["running"]:
                        raise BackupError("WRITERS_STILL_RUNNING")
                created = self.now().isoformat()
                with (candidate / "postgres.dump").open("xb") as output:
                    self.run(["runuser", "-u", "postgres", "--", "pg_dump", "--no-password", "--dbname=mon_kado",
                              "--format=custom", "--compress=0", "--no-owner", "--no-acl"], output=output)
                copy_files(images, candidate / "images", NATIVE_IMAGE_NAME)
                for name in FILES:
                    destination = candidate / "configuration" / name
                    destination.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                    shutil.copyfile(ROOT / name, destination)
                shutil.copyfile(PRODUCTION_ENV, candidate / "configuration/production.env")
                shutil.copyfile(caddy_environment, candidate / "configuration/caddy.env")
                shutil.copyfile(DEPLOYMENT / CURRENT_ENV_NAME, candidate / "configuration/current.env")
                shutil.copyfile(DEPLOYMENT / "active.env", candidate / "configuration/active.env")
                copy_deployment_private(PRODUCTION_ENV.parent, DEPLOYMENT, candidate / "configuration")
                binary_target = candidate / "binaries"
                binary_target.mkdir(mode=0o700)
                for component in ("api", "worker", "migrate"):
                    shutil.copyfile(directory / component, binary_target / component)
                version = self.database("SHOW server_version;").split()[0]
                create_native_manifest(candidate, created, postgres["image"], version, caddy_digest)
                verify_manifest(candidate)
            finally:
                self.recover()
            pending = STATE / "pending"
            if pending.exists():
                pending.rename(STATE / PREVIOUS_NAME)
            candidate.rename(pending)
            self.recover_capture()
            self.update(lastCapture=created, captureBytes=sum(x.stat().st_size for x in pending.rglob("*") if x.is_file()))

    def remove_capture(self, path):
        """Delete only explicitly owned staging directories, never live native storage."""
        if path not in (STATE / "pending", STATE / CANDIDATE_NAME, STATE / PREVIOUS_NAME) or path.is_symlink():
            raise BackupError("UNSAFE_CLEANUP_TARGET")
        shutil.rmtree(path)

    def recover_capture(self):
        """Preserve the previous valid capture across interruption of directory promotion."""
        previous = STATE / PREVIOUS_NAME
        if not previous.exists():
            return
        pending = STATE / "pending"
        if pending.exists():
            verify_manifest(pending)
            self.remove_capture(previous)
            return
        previous.rename(pending)

    def transfer(self):
        """Publish one verified snapshot; never prune after a failed upload or check."""
        self.recover_capture()
        pending = STATE / "pending"
        manifest = verify_manifest(pending)
        for attempt in range(3):
            try:
                private_file(SETTINGS / RCLONE_CONFIG_NAME)
                quota = json.loads(self.run(["rclone", "--config", str(SETTINGS / RCLONE_CONFIG_NAME),
                                             "about", "monkado:", "--json"]))
                required = sum(item["bytes"] for item in manifest["files"].values()) + 512 * 1024 * 1024
                if not isinstance(quota.get("free"), int) or quota["free"] < required:
                    raise BackupError("INSUFFICIENT_REMOTE_QUOTA")
                backup_time = timestamp(manifest["createdAt"]).strftime("%Y-%m-%d %H:%M:%S")
                report = self.restic("backup", ".", "--json", "--host", "monkado-prod",
                                     "--tag", TAG, "--time", backup_time, cwd=pending)
                summaries = [json.loads(line) for line in report.splitlines()]
                summary = next(x for x in summaries if x.get("message_type") == "summary")
                identifier = snapshot_id(summary["snapshot_id"])
                self.restic("check")
                remote = json.loads(self.restic("dump", identifier, "manifest.json"))
                if remote != manifest:
                    raise BackupError("REMOTE_MANIFEST_MISMATCH")
                break
            except BackupError as error:
                self.update(error=error.code, transferAttempt=attempt + 1)
                if attempt < 2:
                    self.wait(900)
        else:
            raise BackupError("REMOTE_BACKUP_FAILED")
        self.update(lastRemoteSuccess=self.now().isoformat(), lastRemoteCapture=manifest["createdAt"],
                    snapshotId=identifier, error=None)
        snapshots = json.loads(self.restic("snapshots", "--json"))
        expired = expired_snapshots(snapshots, identifier, self.now())
        if expired:
            self.restic("forget", *expired)
            self.restic("prune", "--max-repack-size", "128M")
            self.restic("check")
        self.remove_capture(pending)

    def resume_capture(self):
        """Promote a validated interrupted capture without stopping application services."""
        with lock(DEPLOYMENT / "backup-coordination.lock"), lock(DEPLOYMENT / "deploy.lock"):
            if (STATE / MAINTENANCE_NAME).exists() or (DEPLOYMENT / "in-progress.env").exists():
                raise BackupError("RECOVERY_REQUIRED_BEFORE_TRANSFER")
            self.recover_capture()
            pending = STATE / "pending"
            if not pending.exists():
                candidate = STATE / CANDIDATE_NAME
                if not candidate.exists():
                    return False
                manifest = verify_manifest(candidate)
                candidate.rename(pending)
                self.update(lastCapture=manifest["createdAt"],
                            captureBytes=sum(item["bytes"] for item in manifest["files"].values()))
        self.transfer()
        return True

    def check(self):
        """Read every encrypted pack weekly, not only the repository metadata."""
        self.restic("check", "--read-data")
        self.update(lastIntegrityCheck=self.now().isoformat())

    def status(self):
        """Report staleness without exposing private paths, credentials or member data."""
        value = self.state()
        value["health"] = status_health(value, self.now())
        value["missedScheduledCapture"] = missed_capture(value, self.now())
        return value
