"""Linux host orchestration. Provider access is delegated to Restic/rclone."""

import contextlib
import fcntl
import json
import os
import shutil
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

from .capture import FILES, IMAGE_NAME, KEY_NAME, atomic_json, copy_files, create_manifest, verify_manifest
from .policy import BackupError, TAG, expired_snapshots, release_metadata, snapshot_id, status_health, timestamp, missed_capture

ROOT = Path("/opt/monkado")
STATE = Path("/var/lib/monkado-backup")
SETTINGS = Path("/etc/monkado-backup")
DEPLOYMENT = Path("/var/lib/monkado-deployment")
PRODUCTION_ENV = Path("/etc/monkado/production.env")
SERVICES = ("api", "worker", "caddy")


def utcnow():
    """Return an aware UTC timestamp for operational state."""
    return datetime.now(timezone.utc)


def command(arguments, *, output=None, input_file=None, timeout=600, cwd=None, extra_env=None):
    """Never relay subprocess stderr, which can contain credentials or data."""
    environment = {"PATH": "/usr/local/bin:/usr/bin:/bin", "HOME": "/root", "LANG": "C.UTF-8", "TZ": "UTC"}
    environment.update(extra_env or {})
    try:
        result = subprocess.run(arguments, stdout=output or subprocess.PIPE, stdin=input_file or subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL, timeout=timeout,
                                check=False, env=environment, cwd=cwd)
    except subprocess.TimeoutExpired:
        raise BackupError("COMMAND_TIMEOUT") from None
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

    def __init__(self, run=command, now=utcnow, wait=time.sleep):
        self.run = run
        self.now = now
        self.wait = wait

    def state(self):
        """Read bounded operational metadata, never secret-bearing captures."""
        path = STATE / "status.json"
        return json.loads(path.read_text()) if path.exists() else {}

    def update(self, **changes):
        """Publish operational progress atomically."""
        value = self.state()
        value.update(changes)
        atomic_json(STATE / "status.json", value)

    def compose(self, *arguments):
        """Pin Compose to the installed root-owned configuration and current images."""
        return self.run([
            "docker", "compose", "--project-name", "mon-kado", "--project-directory", str(ROOT),
            "--env-file", str(PRODUCTION_ENV), "--env-file", str(DEPLOYMENT / "current.env"),
            "-f", str(ROOT / "compose.yaml"), "-f", str(ROOT / "deployments/production/compose.production.yaml"),
            *arguments,
        ])

    def container(self, service):
        """Resolve exactly one running container in the production project."""
        ids = self.compose("ps", "-q", service).split()
        if len(ids) != 1:
            raise BackupError("SERVICE_NOT_RUNNING")
        return ids[0]

    def inspect(self, identifier, template):
        """Inspect only explicit non-secret fields, never a complete container."""
        return self.run(["docker", "inspect", "--format", template, identifier]).strip()

    def volume(self, name):
        """Use fixed project volume names; never accept user-provided host paths."""
        path = Path(self.run(["docker", "volume", "inspect", "--format", "{{.Mountpoint}}", "mon-kado_" + name]).strip())
        if not path.is_absolute() or not path.is_dir() or path.is_symlink():
            raise BackupError("INVALID_VOLUME_PATH")
        return path

    def restic(self, *arguments, cwd=None):
        """Use a dedicated repository and credential files, without secret arguments."""
        for name in ("password", "rclone.conf", "repository"):
            private_file(SETTINGS / name)
        repository = (SETTINGS / "repository").read_text().strip()
        if repository != "rclone:monkado:MonKado-backups/production-v1":
            raise BackupError("UNEXPECTED_REPOSITORY")
        return self.run(["restic", "--repository-file", str(SETTINGS / "repository"),
                         "--password-file", str(SETTINGS / "password"), "--cache-dir", str(STATE / "cache"),
                         "-o", "rclone.args=serve restic --stdio --drive-use-trash=false", *arguments],
                        timeout=3600, cwd=cwd,
                        extra_env={"RCLONE_CONFIG": str(SETTINGS / "rclone.conf"), "GOMEMLIMIT": "128MiB", "GOMAXPROCS": "2"})

    def recover(self):
        """Restart only services stopped by this tool, including after termination."""
        marker = STATE / "maintenance.json"
        if not marker.exists():
            return
        if json.loads(marker.read_text()) != {"services": list(SERVICES)}:
            raise BackupError("INVALID_MAINTENANCE_MARKER")
        # Compose start traverses dependencies, including the removed one-shot migration.
        # Resolve the stopped application containers, then start exactly those instances.
        identifiers = []
        for service in SERVICES:
            matches = self.compose("ps", "--all", "-q", service).split()
            if len(matches) != 1:
                raise BackupError("RECOVERY_CONTAINER_MISSING_OR_AMBIGUOUS")
            identifiers.append(matches[0])
        self.run(["docker", "start", *identifiers])
        for _ in range(30):
            try:
                self.run(["curl", "--fail", "--silent", "--show-error", "--max-time", "5",
                          "--resolve", "api.monkado.fr:443:127.0.0.1", "https://api.monkado.fr/readiness"])
                worker = self.container("worker")
                if self.inspect(worker, "{{.State.Running}}") != "true":
                    raise BackupError("WORKER_NOT_RUNNING")
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
            candidate = STATE / "capture.new"
            # A completed capture may survive a failed restart or interrupted promotion.
            # Require its explicit transfer before another maintenance can replace it.
            if (candidate / "manifest.json").exists():
                raise BackupError("INTERRUPTED_CAPTURE_REQUIRES_TRANSFER")
            private_file(PRODUCTION_ENV)
            release_metadata((DEPLOYMENT / "current.env").read_text())
            for service in SERVICES:
                self.container(service)
            postgres = self.container("postgres")
            caddy_id = self.inspect(self.container("caddy"), "{{.Image}}")
            caddy_digest = self.inspect(caddy_id, "{{index .RepoDigests 0}}")
            images = self.volume("gift_images")
            keys = self.volume("data_protection_keys")
            size = int(self.run(["docker", "exec", postgres, "sh", "-c",
                                 'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "SELECT pg_database_size(current_database())"']))
            size += sum(p.stat().st_size for source in (images, keys) for p in source.rglob("*") if p.is_file())
            if shutil.disk_usage(STATE).free < size * 2 + 512 * 1024 * 1024:
                raise BackupError("INSUFFICIENT_LOCAL_SPACE")
            if candidate.exists():
                self.remove_capture(candidate)
            candidate.mkdir(mode=0o700)
            atomic_json(STATE / "maintenance.json", {"services": list(SERVICES)})
            try:
                self.compose("stop", "--timeout", "60", "caddy", "api", "worker")
                for service in SERVICES:
                    if self.compose("ps", "--status", "running", "-q", service).strip():
                        raise BackupError("WRITERS_STILL_RUNNING")
                created = self.now().isoformat()
                with (candidate / "postgres.dump").open("xb") as output:
                    self.run(["docker", "exec", postgres, "sh", "-c",
                              'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --compress=0 --no-owner --no-acl'], output=output)
                copy_files(images, candidate / "images", IMAGE_NAME)
                copy_files(keys, candidate / "keys", KEY_NAME)
                for name in FILES:
                    destination = candidate / "configuration" / name
                    destination.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                    shutil.copyfile(ROOT / name, destination)
                shutil.copyfile(PRODUCTION_ENV, candidate / "configuration/production.env")
                shutil.copyfile(DEPLOYMENT / "current.env", candidate / "configuration/current.env")
                pg_image_id = self.inspect(postgres, "{{.Image}}")
                pg_digest = self.inspect(pg_image_id, "{{index .RepoDigests 0}}")
                version = self.run(["docker", "exec", postgres, "sh", "-c",
                                    'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "SHOW server_version"']).strip().split()[0]
                create_manifest(candidate, created, pg_digest, version, caddy_digest)
                verify_manifest(candidate)
            finally:
                self.recover()
            pending = STATE / "pending"
            if pending.exists():
                pending.rename(STATE / "pending.previous")
            candidate.rename(pending)
            self.recover_capture()
            self.update(lastCapture=created, captureBytes=sum(x.stat().st_size for x in pending.rglob("*") if x.is_file()))

    def remove_capture(self, path):
        """Delete only explicitly owned staging directories; never Docker volumes."""
        if path not in (STATE / "pending", STATE / "capture.new", STATE / "pending.previous") or path.is_symlink():
            raise BackupError("UNSAFE_CLEANUP_TARGET")
        shutil.rmtree(path)

    def recover_capture(self):
        """Preserve the previous valid capture across interruption of directory promotion."""
        previous = STATE / "pending.previous"
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
                private_file(SETTINGS / "rclone.conf")
                quota = json.loads(self.run(["rclone", "--config", str(SETTINGS / "rclone.conf"),
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
            if (STATE / "maintenance.json").exists() or (DEPLOYMENT / "in-progress.env").exists():
                raise BackupError("RECOVERY_REQUIRED_BEFORE_TRANSFER")
            self.recover_capture()
            pending = STATE / "pending"
            if not pending.exists():
                candidate = STATE / "capture.new"
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
