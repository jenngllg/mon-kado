"""Verify durable intent using actual private files, not a mocked filesystem."""

import os
from pathlib import Path
import stat
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_store import CoordinatedStore, MAXIMUM_INTENT_BYTES
from deploy_policy import DeploymentError


@unittest.skipUnless(hasattr(os, "geteuid") and os.geteuid() == 0, "requires Linux root-owned fixture files")
class CoordinatedStoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.store = CoordinatedStore(self.directory)

    def test_save_when_replaced_persists_private_complete_intent(self):
        self.assertIsNone(self.store.load())
        self.store.save({"phase": "prepared"})
        self.assertEqual(self.store.load(), {"phase": "prepared"})
        self.assertEqual(stat.S_IMODE(self.store.path.stat().st_mode), 0o600)
        self.store.save({"phase": "committed", "version": 1})
        self.assertEqual(self.store.load(), {"phase": "committed", "version": 1})
        self.store.clear()
        self.assertIsNone(self.store.load())

    def test_load_and_clear_when_file_is_not_private_refuse_it(self):
        self.store.save({"phase": "prepared"})
        self.store.path.chmod(0o644)
        with self.assertRaises(DeploymentError):
            self.store.load()
        with self.assertRaises(DeploymentError):
            self.store.clear()
        self.assertTrue(self.store.path.exists())

    def test_operations_when_intent_is_symlink_preserve_target(self):
        target = self.directory / "target"
        target.write_text("unchanged", encoding="utf-8")
        self.store.path.symlink_to(target)
        for operation in (self.store.load, self.store.clear, lambda: self.store.save({"phase": "prepared"})):
            with self.subTest(operation=operation), self.assertRaises(DeploymentError):
                operation()
        self.assertEqual(target.read_text(encoding="utf-8"), "unchanged")
        self.store.path.unlink()
        self.store.path.symlink_to(self.directory / "missing")
        with self.assertRaises(DeploymentError):
            self.store.load()

    def test_save_when_intent_exceeds_limit_preserves_previous_file(self):
        self.store.save({"phase": "prepared"})
        with self.assertRaises(DeploymentError):
            self.store.save({"value": "a" * MAXIMUM_INTENT_BYTES})
        self.assertEqual(self.store.load(), {"phase": "prepared"})

    def test_complete_when_repeated_preserves_exact_snapshot_receipt(self):
        intent = {"previous": {"backend": {"revision": "a" * 40}, "frontend": {"revision": "b" * 40}},
                  "candidate": {"backend": {"revision": "c" * 40, "publicationId": "42-1"},
                                "frontend": {"revision": "d" * 40}},
                  "backup": "e" * 64, "frontendBackup": "f" * 64, "toolDigest": "0" * 64}
        self.store.complete(intent)
        self.store.complete(intent)
        target = self.directory / "coordinated-receipt-42-1.json"
        import json
        self.assertEqual(json.loads(target.read_bytes())["frontendSnapshot"], "f" * 64)
        self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o600)
        with self.assertRaisesRegex(DeploymentError, "TRANSITION_RECEIPT_CHANGED"):
            self.store.complete(intent | {"frontendBackup": "1" * 64})


if __name__ == "__main__":
    unittest.main()
