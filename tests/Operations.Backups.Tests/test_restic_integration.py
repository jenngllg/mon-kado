"""Real local encrypted repository tests; no Drive credentials or network calls."""

import json
import secrets
import shutil
import tempfile
import unittest
from pathlib import Path

from fixtures import capture
from monkado_backup.capture import verify_manifest
from monkado_backup.operations import command
from monkado_backup.policy import BackupError, snapshot_id


@unittest.skipUnless(shutil.which("restic"), "Restic must be installed for the local integration exercise")
class ResticIntegrationTests(unittest.TestCase):
    def test_encrypted_roundtrip_deduplication_and_wrong_password(self):
        # Arrange
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "capture"
            source.mkdir()
            manifest = capture(source)
            password = root / "password"
            password.write_text(secrets.token_hex(32))
            password.chmod(0o600)
            prefix = ["restic", "--repo", str(root / "repository"), "--password-file", str(password)]
            command(prefix + ["init"])

            # Act
            first = command(prefix + ["backup", ".", "--json"], cwd=source)
            summary = next(json.loads(line) for line in first.splitlines()
                           if json.loads(line).get("message_type") == "summary")
            identifier = snapshot_id(summary["snapshot_id"])
            second = command(prefix + ["backup", ".", "--json"], cwd=source)
            repeated = next(json.loads(line) for line in second.splitlines()
                            if json.loads(line).get("message_type") == "summary")
            command(prefix + ["check", "--read-data"])
            command(prefix + ["restore", identifier, "--target", str(root / "restored")])

            # Assert
            self.assertEqual(0, repeated["data_added"])
            self.assertEqual(manifest, verify_manifest(root / "restored"))
            self.assertEqual(manifest, json.loads(command(prefix + ["dump", identifier, "manifest.json"])))
            original_secret = password.read_text()
            password.write_text(secrets.token_hex(32))
            with self.assertRaisesRegex(BackupError, "COMMAND_FAILED"):
                command(prefix + ["snapshots", "--json"])
            password.write_text(original_secret)
            pack = next(path for path in (root / "repository/data").rglob("*") if path.is_file())
            with pack.open("r+b") as stream:
                original = stream.read(1)
                stream.seek(0)
                stream.write(bytes([original[0] ^ 1]))
            with self.assertRaisesRegex(BackupError, "COMMAND_FAILED"):
                command(prefix + ["check", "--read-data"])
