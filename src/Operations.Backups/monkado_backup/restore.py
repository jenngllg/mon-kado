"""Explicit snapshot downloads and non-destructive, network-isolated restoration."""

import re
import secrets
import tempfile
from pathlib import Path

from .capture import verify_manifest
from .policy import BackupError, snapshot_id

RESTORE_LABEL = "monkado.restore=true"


class Restore:
    """Create new resources only; never start application workers or use production secrets."""

    def __init__(self, operations):
        self.operations = operations
        self.run = operations.run

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
        """Restore a PostgreSQL 18 database and immutable files into fresh resources."""
        if not re.fullmatch(r"monkado-restore-[a-z0-9]{8,32}", name):
            raise BackupError("INVALID_RESTORE_PROJECT")
        capture = Path(capture).absolute()
        manifest = verify_manifest(capture)
        names = [name + "-postgres", name + "-images", name + "-keys"]
        existing_volumes = set(self.run(["docker", "volume", "ls", "--format", "{{.Name}}"]).splitlines())
        existing_networks = set(self.run(["docker", "network", "ls", "--format", "{{.Name}}"]).splitlines())
        existing_containers = set(self.run(["docker", "ps", "-a", "--format", "{{.Names}}"]).splitlines())
        if existing_volumes.intersection(names) or name in existing_networks or name + "-postgres" in existing_containers:
            raise BackupError("RESTORE_RESOURCES_EXIST")
        image = manifest["postgresImage"]
        # Pull before creating any resources; subsequent helpers cannot contact the network.
        self.run(["docker", "pull", image])
        for volume in names:
            self.run(["docker", "volume", "create", "--label", RESTORE_LABEL, volume])
        self.run(["docker", "network", "create", "--internal", "--label", RESTORE_LABEL, name])
        with tempfile.TemporaryDirectory(prefix="monkado-restore-") as temporary:
            environment = Path(temporary) / "database.env"
            environment.write_text("POSTGRES_DB=mon_kado\nPOSTGRES_USER=mon_kado\nPOSTGRES_PASSWORD=" + secrets.token_hex(32) + "\n")
            environment.chmod(0o600)
            self.run(["docker", "run", "-d", "--name", name + "-postgres", "--network", name,
                      "--label", RESTORE_LABEL, "--env-file", str(environment),
                      "--mount", "type=volume,src=" + names[0] + ",dst=/var/lib/postgresql",
                      "--health-cmd", "pg_isready -h 127.0.0.1 -U mon_kado -d mon_kado", "--health-interval", "1s",
                      "--health-retries", "60", image])
        try:
            self.populate(capture, name, names, image)
        finally:
            # Even an unsuccessful exercise must not leave a database running.
            self.run(["docker", "stop", name + "-postgres"])
        return {"project": name, "revision": manifest["release"]["RELEASE_REVISION"],
                "applicationStarted": False, "databaseStopped": True}

    def populate(self, capture, name, names, image):
        """Import the capture only after the isolated database reports readiness."""
        for _ in range(60):
            if self.operations.inspect(name + "-postgres", "{{.State.Health.Status}}") == "healthy":
                break
            self.operations.wait(1)
        else:
            raise BackupError("RESTORE_DATABASE_NOT_READY")
        with (capture / "postgres.dump").open("rb") as dump:
            self.run(["docker", "exec", "-i", name + "-postgres", "pg_restore", "--exit-on-error",
                      "--single-transaction", "--no-owner", "--no-acl", "-U", "mon_kado", "-d", "mon_kado"], input_file=dump)
        for folder, volume in (("images", names[1]), ("keys", names[2])):
            helper = self.run(["docker", "create", "--network", "none", "--read-only",
                               "--security-opt", "no-new-privileges:true", "--entrypoint", "sh",
                               "--mount", "type=volume,src=" + volume + ",dst=/target", image, "-c",
                               "chown -R 1654:1654 /target && chmod -R u=rwX,go= /target"]).strip()
            try:
                self.run(["docker", "cp", str(capture / folder) + "/.", helper + ":/target"])
                self.run(["docker", "start", "--attach", helper])
            finally:
                self.run(["docker", "rm", helper])
