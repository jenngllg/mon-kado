"""Manifest and filesystem security tests."""

import json
import runpy
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from fixtures import capture
from monkado_backup.capture import IMAGE_NAME, KEY_NAME, atomic_json, copy_files, copy_deployment_private, create_manifest, entries, regular, validate_shape, verify_manifest
from monkado_backup.policy import BackupError, FILES, LEGACY_FILES, PREVIOUS_FILES


class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.value = capture(self.root)

    def test_manifest_round_trip(self):
        # Arrange / Act / Assert
        self.assertEqual(self.value, verify_manifest(self.root))

    def test_configuration_inventory_matches_current_release_contract(self):
        # Arrange
        repository = Path(__file__).resolve().parents[2]
        release = runpy.run_path(str(repository / "deployments/production/release_manifest.py"))
        # Act / Assert
        self.assertEqual(set(release["FILES"]), set(FILES))

    def test_changed_and_missing_bytes_are_rejected(self):
        # Arrange
        (self.root / "postgres.dump").write_bytes(b"changed")
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "INTEGRITY"):
            verify_manifest(self.root)
        (self.root / "postgres.dump").unlink()
        with self.assertRaisesRegex(BackupError, "INCOMPLETE"):
            verify_manifest(self.root)

    def test_unexpected_secret_is_never_accepted(self):
        # Arrange
        (self.root / "password").write_text("do not include")
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "UNEXPECTED"):
            verify_manifest(self.root)

    def test_keys_are_mandatory(self):
        # Arrange
        inventory = {name: value for name, value in self.value["files"].items() if not name.startswith("keys/")}
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "MISSING_DATA_PROTECTION"):
            validate_shape(inventory)

    def test_manifest_contract_is_strict(self):
        # Arrange / Act / Assert
        for change in ({"schemaVersion": True}, {"schemaVersion": 4}, {"extra": 1},
                       {"caddyImage": None}, {"caddyImage": "caddy:latest"},
                       {"postgresImage": None}, {"postgresImage": "postgres:latest"},
                       {"postgresVersion": None}, {"postgresVersion": "17.1"}, {"postgresVersion": "18.\u0666"}, {"release": {}}):
            with self.subTest(change=change):
                (self.root / "manifest.json").write_text(json.dumps({**self.value, **change}))
                with self.assertRaises(BackupError):
                    verify_manifest(self.root)
        (self.root / "manifest.json").write_text("[]")
        with self.assertRaises(BackupError):
            verify_manifest(self.root)

    def test_create_rejects_incompatible_database(self):
        # Arrange / Act / Assert
        for image, version in (("postgres:18", "18.6"), ("postgres@sha256:" + "d" * 64, "17.1"),
                               ("postgres@sha256:" + "d" * 64, "18.\u0666")):
            with self.subTest(version=version), self.assertRaises(BackupError):
                create_manifest(self.root, self.value["createdAt"], image, version, self.value["caddyImage"])

    def test_create_rejects_mutable_or_missing_caddy_reference(self):
        # Arrange / Act / Assert
        for image in (None, "caddy:latest"):
            with self.subTest(image=image), self.assertRaisesRegex(BackupError, "INVALID_CADDY_IMAGE"):
                create_manifest(self.root, self.value["createdAt"], self.value["postgresImage"], "18.6", image)

    def test_legacy_snapshot_remains_verifiable_without_new_configuration(self):
        # Arrange
        for name in set(FILES) - set(LEGACY_FILES):
            (self.root / "configuration" / name).unlink()
        value = self.value | {"schemaVersion": 1, "files": entries(self.root)}
        del value["caddyImage"]
        atomic_json(self.root / "manifest.json", value)
        # Act / Assert
        self.assertEqual(value, verify_manifest(self.root))

    def test_current_capture_requires_frontend_and_monitoring_configuration(self):
        # Arrange
        (self.root / "configuration/deployments/frontend/frontend.caddy").unlink()
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "INCOMPLETE_CAPTURE"):
            verify_manifest(self.root)

    def test_v2_capture_remains_readable_without_deployment_modules(self):
        for name in set(FILES) - set(PREVIOUS_FILES):
            (self.root / "configuration" / name).unlink()
        value = self.value | {"schemaVersion": 2, "files": entries(self.root)}
        atomic_json(self.root / "manifest.json", value)
        self.assertEqual(value, verify_manifest(self.root))

    def test_deployment_credentials_are_private_and_only_allowed_in_v3(self):
        private = self.root / "private"
        private.mkdir()
        target = self.root / "configuration"
        copy_deployment_private(private, private, target)
        secret = private / "deployment-smoke.json"
        secret.write_text("synthetic credential fixture")
        secret.chmod(0o600)
        (private / "status.json").write_text("deployment fixture")
        (private / "status.json").chmod(0o600)
        copy_deployment_private(private, private, target)
        self.assertEqual(0o600, (target / secret.name).stat().st_mode & 0o777)
        secret.chmod(0o644)
        with self.assertRaisesRegex(BackupError, "UNSAFE_DEPLOYMENT_FILE"):
            copy_deployment_private(private, private, target)
        secret.unlink()
        secret.symlink_to(private / "missing")
        with self.assertRaisesRegex(BackupError, "UNSAFE_FILE"):
            copy_deployment_private(private, private, target)
        paths = set(self.value["files"]) | {"configuration/deployment-smoke.json", "configuration/deployment-status.json"}
        validate_shape(paths, 3)
        with self.assertRaisesRegex(BackupError, "UNEXPECTED_CAPTURE_FILE"):
            validate_shape(paths, 2)

    def test_copies_shards_and_pending_but_not_temporary_files(self):
        # Arrange
        source = self.root / "images"
        folder = source / "01/ab"
        stem = "01ab" + "c" * 28
        (folder / (stem + ".pending")).write_text("")
        (folder / (stem + ".webp." + "d" * 32 + ".tmp")).write_text("interrupted")
        (folder / "foreign.txt").write_text("foreign")
        target = self.root / "copy"
        # Act
        copy_files(source, target, IMAGE_NAME)
        # Assert
        self.assertEqual({f"01/ab/{stem}.webp", f"01/ab/{stem}.pending"}, set(entries(target)))
        key_target = self.root / "key-copy"
        (self.root / "keys/revocation-20260921.xml").write_text("<revocation/>")
        copy_files(self.root / "keys", key_target, KEY_NAME)
        self.assertEqual(2, len(entries(key_target)))
        self.assertIn("revocation-20260921.xml", entries(key_target))

    def test_symlinks_are_rejected(self):
        # Arrange
        link = self.root / "link"
        link.symlink_to(self.root / "postgres.dump")
        # Act / Assert
        with self.assertRaises(BackupError):
            entries(self.root)
        link.unlink()
        (self.root / "postgres.dump").unlink()
        (self.root / "postgres.dump").symlink_to(self.root / "manifest.json")
        with self.assertRaises(BackupError):
            verify_manifest(self.root)
        with self.assertRaises(BackupError):
            regular(self.root)

    def test_atomic_write_preserves_old_status_when_replace_fails(self):
        # Arrange
        path = self.root / "status.json"
        atomic_json(path, {"success": True})
        # Act / Assert
        with patch("monkado_backup.capture.os.replace", side_effect=OSError()), self.assertRaises(OSError):
            atomic_json(path, {"success": False})
        self.assertEqual({"success": True}, json.loads(path.read_text()))
        self.assertEqual([], list(self.root.glob(".state-*")))
