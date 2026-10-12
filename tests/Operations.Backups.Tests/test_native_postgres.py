"""Explicit native PostgreSQL restoration uses the real Go migrator and disposable Unix sockets."""

import base64
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import exercise_drive

from fixtures import native_capture
from monkado_backup.capture import create_native_manifest, digest
from monkado_backup.operations import Operations, command
from monkado_backup.restore import Restore, PG_BIN, SOCKET_PORT


@unittest.skipUnless(os.environ.get("MONKADO_NATIVE_BACKUP_TESTS") == "1", "Explicit native database restoration exercise only")
class NativePostgresTests(unittest.TestCase):
    def test_native_qualification_preserves_protected_bytes_with_a_local_encrypted_provider(self):
        # Arrange: replace only the external provider repository; all native effects remain real.
        self.assertEqual(0, os.geteuid())
        migrator = Path(os.environ["MONKADO_NATIVE_MIGRATOR"]).resolve()
        with tempfile.TemporaryDirectory(prefix="mk-qual-") as temporary:
            root = Path(temporary)
            root.chmod(0o711)
            work = root / "exercise"
            work.mkdir(mode=0o711)
            password = root / "password"
            password.write_text("synthetic-local-qualification-password")
            password.chmod(0o600)
            repository = root / "encrypted-repository"

            def local_provider(arguments, **options):
                if arguments[0] == "restic":
                    arguments = list(arguments)
                    self.assertRegex(arguments[arguments.index("--repo") + 1],
                                     r"^rclone:monkado:MonKado-backups/exercise-native-[a-f0-9]{16}$")
                    arguments[arguments.index("--repo") + 1] = str(repository)
                    arguments[arguments.index("--password-file") + 1] = str(password)
                return command(arguments, **options)

            # Act
            with patch.dict(os.environ, {"MONKADO_DRIVE_EXERCISE": "1", "MONKADO_NATIVE_MIGRATOR": str(migrator)}), \
                    patch.object(exercise_drive, "private_file"), \
                    patch.object(exercise_drive.tempfile, "mkdtemp", return_value=str(work)), \
                    patch.object(exercise_drive, "command", side_effect=local_provider):
                exercise_drive.main()
            # Assert
            result = json.loads((work / "result.json").read_bytes())
            self.assertTrue(result["deduplicationVerified"])
            self.assertTrue(result["hashesVerified"])
            self.assertTrue(result["databaseStopped"])
            self.assertFalse(result["applicationStarted"])
            self.assertEqual(64, len(result["snapshotId"]))

    def test_complete_go_schema_and_private_files_restore_into_a_new_stopped_native_cluster(self):
        self.assertEqual(0, os.geteuid())
        migrator = Path(os.environ["MONKADO_NATIVE_MIGRATOR"]).resolve()
        self.assertTrue(migrator.is_file())
        with tempfile.TemporaryDirectory(prefix="monkado-native-backup-") as temporary:
            root = Path(temporary)
            root.chmod(0o711)
            binary = root / "migrate"
            shutil.copyfile(migrator, binary)
            binary.chmod(0o755)
            metadata = json.loads(command([str(binary), "--version"]))
            catalog = json.loads(command([str(binary), "--catalog"]))
            captured = root / "capture"
            captured.mkdir(mode=0o700)
            fixture = native_capture(captured, catalog=catalog, migrator=binary, revision=metadata["revision"])
            source = root / "source"
            socket = root / "socket"
            prefix = ["runuser", "-u", "postgres", "--"]
            command(["install", "-d", "-m", "0700", "-o", "postgres", "-g", "postgres", str(source), str(socket)])
            command([*prefix, str(PG_BIN / "initdb"), "--pgdata=" + str(source), "--auth-local=peer",
                     "--auth-host=reject", "--encoding=UTF8", "--locale=C.UTF-8", "--no-instructions"])
            control = [*prefix, str(PG_BIN / "pg_ctl"), "--pgdata=" + str(source), "--wait", "--timeout=60"]
            options = "-c listen_addresses='' -c unix_socket_directories=" + str(socket) + " -c unix_socket_permissions=0700 -c port=" + SOCKET_PORT + " -c shared_buffers=32MB -c max_connections=10"
            command([*control, "start", "--log=" + str(source / "startup.log"), "--options=" + options])
            try:
                connection = ["--host=" + str(socket), "--port=" + SOCKET_PORT, "--username=postgres", "--no-password"]
                command([*prefix, str(PG_BIN / "createdb"), *connection, "--template=template0", "mon_kado_fixture"])
                runtime = {"DATABASE_URL": "postgres://postgres@/mon_kado_fixture?host=" + str(socket) + "&port=" + SOCKET_PORT + "&sslmode=disable",
                           "JWT_SIGNING_KEY": base64.b64encode(bytes(range(32))).decode(),
                           "DATA_ENCRYPTION_KEY": base64.b64encode(bytes(range(32, 64))).decode(),
                           "APP_ENV": "local", "EMAIL_PROVIDER": "Disabled"}
                command([*prefix, str(binary)], extra_env=runtime)
                psql = [*prefix, str(PG_BIN / "psql"), *connection, "--dbname=mon_kado_fixture", "-X", "-At", "-v", "ON_ERROR_STOP=1", "-c"]
                command([*psql, "CREATE TABLE backup_probe(id integer PRIMARY KEY, payload text NOT NULL); INSERT INTO backup_probe VALUES(1,'native-roundtrip-fixture');"])
                with (captured / "postgres.dump").open("wb") as output:
                    command([*prefix, str(PG_BIN / "pg_dump"), *connection, "--dbname=mon_kado_fixture",
                             "--format=custom", "--compress=0", "--no-owner", "--no-acl"], output=output)
                manifest = create_native_manifest(captured, fixture["createdAt"], fixture["postgresSHA256"], "18.6", fixture["caddySHA256"])
            finally:
                command([*control, "stop", "--mode=fast"])
            restore = Restore(Operations(), root=root / "restores")
            name = "monkado-restore-" + secrets.token_hex(8)
            result = restore.volumes(captured, name)
            self.assertTrue(result["databaseStopped"])
            self.assertFalse(result["applicationStarted"])
            target = restore.root / name
            restored_control = [*prefix, str(PG_BIN / "pg_ctl"), "--pgdata=" + str(target / "postgres"), "--wait", "--timeout=60"]
            self.assertNotEqual(0, subprocess.run([*restored_control, "status"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False).returncode)
            command([*restored_control, "start", "--log=" + str(target / "postgres/verification.log")])
            try:
                restored_connection = ["--host=" + str(target / "socket"), "--port=" + SOCKET_PORT,
                                       "--username=postgres", "--no-password"]
                raw = command([*prefix, str(PG_BIN / "psql"), *restored_connection, "--dbname=mon_kado_restore", "-X", "-At",
                               "-v", "ON_ERROR_STOP=1", "-c", "SELECT payload FROM backup_probe WHERE id=1;"])
                self.assertEqual("native-roundtrip-fixture", raw.strip())
                for relative, item in manifest["files"].items():
                    if relative.startswith(("images/", "configuration/", "binaries/")):
                        self.assertEqual(item["sha256"], digest(target / relative))
            finally:
                command([*restored_control, "stop", "--mode=fast"])
