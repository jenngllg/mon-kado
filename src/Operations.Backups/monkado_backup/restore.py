"""Explicit snapshot downloads and non-destructive native PostgreSQL restoration."""

import json
import os
import pwd
import re
import shutil
import stat
from pathlib import Path

from .capture import verify_manifest
from .policy import BackupError, snapshot_id

RESTORES = Path("/var/lib/monkado-restores")
PG_BIN = Path("/usr/lib/postgresql/18/bin")
SOCKET_PORT = "55439"


class Restore:
    """Create private fresh resources only; never start applications or use production credentials."""

    def __init__(self, operations, root=RESTORES):
        self.operations = operations
        self.run = operations.run
        self.root = root

    def download(self, identifier, target):
        """Require a new local directory and validate all restored files before use."""
        snapshot_id(identifier)
        target = Path(target).absolute()
        if target.exists() or target.resolve() != target:
            raise BackupError("RESTORE_TARGET_NOT_EMPTY")
        target.mkdir(mode=0o700)
        self.operations.restic("restore", identifier, "--target", str(target))
        return verify_manifest(target)

    def volumes(self, capture, name):
        """Retain the administration command while restoring into an isolated native cluster."""
        if not re.fullmatch(r"monkado-restore-[a-z0-9]{8,32}", name):
            raise BackupError("INVALID_RESTORE_PROJECT")
        capture = Path(capture).absolute()
        metadata = capture.lstat()
        if not stat.S_ISDIR(metadata.st_mode) or metadata.st_uid != 0 or stat.S_IMODE(metadata.st_mode) != 0o700:
            raise BackupError("UNSAFE_RESTORE_CAPTURE")
        manifest = verify_manifest(capture)
        if manifest["schemaVersion"] != 5:
            raise BackupError("NATIVE_CAPTURE_REQUIRED")
        self.root.mkdir(mode=0o711, parents=True, exist_ok=True)
        metadata = self.root.lstat()
        if (not self.root.is_absolute() or self.root.resolve() != self.root or
                not stat.S_ISDIR(metadata.st_mode) or metadata.st_uid != 0 or metadata.st_mode & 0o022):
            raise BackupError("UNSAFE_RESTORE_ROOT")
        if re.fullmatch(r"/[A-Za-z0-9/_-]+", str(self.root)) is None:
            raise BackupError("UNSAFE_RESTORE_ROOT")
        # CLI umask 077 must not prevent PostgreSQL traversing otherwise private parents.
        self.root.chmod(0o711)
        target = self.root / name
        # Linux sockaddr_un leaves at most 107 bytes for the complete socket filename.
        if len(os.fsencode(target / "socket" / (".s.PGSQL." + SOCKET_PORT))) > 107:
            raise BackupError("RESTORE_SOCKET_PATH_TOO_LONG")
        if target.exists() or target.is_symlink():
            raise BackupError("RESTORE_RESOURCES_EXIST")
        target.mkdir(mode=0o711)
        target.chmod(0o711)
        cluster = target / "postgres"
        socket = target / "socket"
        self.run(["install", "-d", "-m", "0700", "-o", "postgres", "-g", "postgres", str(cluster), str(socket)])
        prefix = ["runuser", "-u", "postgres", "--"]
        self.run([*prefix, str(PG_BIN / "initdb"), "--pgdata=" + str(cluster), "--auth-local=peer", "--auth-host=reject",
                  "--encoding=UTF8", "--locale=C.UTF-8", "--no-instructions"], timeout=120)
        # Persist isolation so a later explicit verification start cannot silently enable TCP.
        settings = cluster / "postgresql.auto.conf"
        settings.write_text("listen_addresses = ''\nunix_socket_directories = '" + str(socket) +
                            "'\nunix_socket_permissions = 0700\nport = " + SOCKET_PORT +
                            "\nshared_buffers = '32MB'\nmax_connections = 10\nwork_mem = '1MB'\ntimezone = 'UTC'\n", encoding="ascii")
        settings.chmod(0o600)
        account = pwd.getpwnam("postgres")
        os.chown(settings, account.pw_uid, account.pw_gid)
        control = [*prefix, str(PG_BIN / "pg_ctl"), "--pgdata=" + str(cluster), "--wait", "--timeout=60"]
        # Paths are fixed beneath a root-owned directory and a strictly validated project identifier.
        # Disable TCP entirely; each project has its own private Unix socket at the same harmless port.
        options = ("-c listen_addresses='' -c unix_socket_directories=" + str(socket) +
                   " -c unix_socket_permissions=0700 -c port=" + SOCKET_PORT +
                   " -c shared_buffers=32MB -c max_connections=10 -c work_mem=1MB")
        try:
            self.run([*control, "start", "--log=" + str(cluster / "startup.log"), "--options=" + options], timeout=75)
            connection = ["--host=" + str(socket), "--port=" + SOCKET_PORT, "--username=postgres", "--no-password"]
            self.run([*prefix, str(PG_BIN / "createdb"), *connection, "--template=template0", "mon_kado_restore"], timeout=60)
            with (capture / "postgres.dump").open("rb") as dump:
                self.run([*prefix, str(PG_BIN / "pg_restore"), *connection, "--dbname=mon_kado_restore",
                          "--exit-on-error", "--single-transaction", "--no-owner", "--no-acl"], input_file=dump, timeout=600)
            raw = self.run([*prefix, str(PG_BIN / "psql"), *connection, "--dbname=mon_kado_restore", "-X", "-At",
                            "-v", "ON_ERROR_STOP=1", "-c", "SELECT name FROM public.mon_kado_migrations ORDER BY name;"], timeout=15)
            state = json.loads((capture / "configuration/deployment-status.json").read_bytes())
            expected = [item["id"] for item in state["current"]["migrationCatalog"]["migrations"]]
            if raw.splitlines() != expected:
                raise BackupError("RESTORED_HISTORY_DIVERGED")
            for folder in ("images", "configuration", "binaries"):
                destination = target / folder
                shutil.copytree(capture / folder, destination)
                destination.chmod(0o700)
                for path in destination.rglob("*"):
                    path.chmod(0o700 if path.is_dir() else 0o600)
        finally:
            # Retain failed resources for inspection, but never leave the isolated database running.
            self.run([*control, "stop", "--mode=fast"], timeout=75)
        return {"project": name, "revision": manifest["release"]["RELEASE_REVISION"],
                "applicationStarted": False, "databaseStopped": True}
