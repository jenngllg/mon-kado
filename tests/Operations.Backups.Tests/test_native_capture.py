"""Native coherent captures preserve approved executables, private configuration and flat image paths."""

import json
from pathlib import Path
import tempfile
import unittest

from fixtures import native_capture
from monkado_backup.capture import NATIVE_IMAGE_NAME, copy_files, entries, verify_manifest
from monkado_backup.policy import BackupError, FILES, LEGACY_NATIVE_FILES


class NativeCaptureTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.manifest = native_capture(self.root)

    def test_native_inventory_verifies_all_executables_and_keeps_private_environment_in_encrypted_capture(self):
        self.assertEqual(self.manifest, verify_manifest(self.root))
        self.assertEqual(6, self.manifest["schemaVersion"])
        self.assertEqual({"binaries/api", "binaries/worker", "binaries/migrate"},
                         {name for name in self.manifest["files"] if name.startswith("binaries/")})
        self.assertIn("configuration/production.env", self.manifest["files"])
        self.assertNotIn("configuration/compose.yaml", self.manifest["files"])
        self.assertFalse(any(name.startswith("keys/") for name in self.manifest["files"]))

    def test_rewritten_binary_cannot_be_accepted_even_after_an_inventory_rewrite(self):
        path = self.root / "binaries/api"
        path.write_bytes(b"wrong native executable")
        value = self.manifest | {"files": entries(self.root)}
        (self.root / "manifest.json").write_text(json.dumps(value))
        with self.assertRaisesRegex(BackupError, "BINARY_INTEGRITY_FAILED"):
            verify_manifest(self.root)

    def test_rewritten_coordinator_cannot_be_accepted_even_after_an_inventory_rewrite(self):
        path = self.root / "configuration/src/Operations.Deployment/coordinated_gate.py"
        path.write_text("changed")
        (self.root / "manifest.json").write_text(json.dumps(self.manifest | {"files": entries(self.root)}))
        with self.assertRaisesRegex(BackupError, "COORDINATOR_INTEGRITY_FAILED"):
            verify_manifest(self.root)

    def test_schema_five_capture_remains_verifiable_after_inventory_upgrade(self):
        # Reconstruct the previous immutable inventory and its original hash.
        for name in set(FILES) - set(LEGACY_NATIVE_FILES):
            (self.root / "configuration" / name).unlink()
        (self.root / "configuration/coordinated-manifest.json").unlink()
        old = self.manifest | {"schemaVersion": 5, "files": entries(self.root)}
        (self.root / "manifest.json").write_text(json.dumps(old))
        self.assertEqual(verify_manifest(self.root), old)

    def test_missing_private_pointer_and_staging_artifacts_are_rejected(self):
        (self.root / "configuration/active.env").unlink()
        with self.assertRaisesRegex(BackupError, "INCOMPLETE_CAPTURE"):
            verify_manifest(self.root)

    def test_flat_native_images_include_pending_markers_without_copying_interrupted_upload_bytes(self):
        images = self.root / "images"
        stem = "019a1b11-2233-7444-8555-66778899aabb"
        (images / (stem + ".pending")).write_bytes(b"")
        (images / (stem + ".webp.partial")).write_bytes(b"interrupted")
        copied = self.root / "copied"
        copy_files(images, copied, NATIVE_IMAGE_NAME)
        self.assertEqual({stem + ".webp", stem + ".pending"}, set(entries(copied)))

    def test_capture_without_stable_data_encryption_key_is_rejected_even_with_a_rewritten_inventory(self):
        environment = self.root / "configuration/production.env"
        environment.write_text("\n".join(line for line in environment.read_text().splitlines()
                                           if not line.startswith("DATA_ENCRYPTION_KEY=")))
        (self.root / "manifest.json").write_text(json.dumps(self.manifest | {"files": entries(self.root)}))
        with self.assertRaisesRegex(ValueError, "NATIVE_RUNTIME_KEYS_INVALID"):
            verify_manifest(self.root)
