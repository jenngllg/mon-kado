"""Private-file and crash-consistency tests run in a disposable Linux container."""

import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from deploy_policy import DeploymentError, dotenv, initial_state
from deploy_storage import Store, atomic_write, private_read
from test_engine import release


class StorageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.store = Store(self.root)

    def test_empty_installation_and_explicit_legacy_adoption(self):
        self.assertEqual(initial_state(), self.store.load())
        atomic_write(self.root / "current.env", dotenv(release()).encode())
        with self.assertRaisesRegex(DeploymentError, "ADOPTION_REQUIRED"):
            self.store.load()

    def test_broken_link_does_not_become_new_installation(self):
        (self.root / "status.json").symlink_to(self.root / "absent")
        with self.assertRaisesRegex(DeploymentError, "UNSAFE_PRIVATE_FILE"):
            self.store.load()

    def test_transition_and_commit_publish_exact_compatibility_files(self):
        value = initial_state() | {"candidate": release(), "phase": "prepared"}
        self.store.begin(value)
        self.assertEqual(value, self.store.load())
        self.assertEqual(dotenv(release()).encode(), private_read(self.root / "in-progress.env"))
        value.update(phase="succeeded", current=release())
        self.store.finish(value)
        self.assertEqual(value, self.store.load())
        self.assertEqual(dotenv(release()).encode(), private_read(self.root / "current.env"))
        self.assertFalse((self.root / "in-progress.env").exists())
        self.store.reconcile(value)
        self.assertEqual(0o600, (self.root / "status.json").stat().st_mode & 0o777)

    def test_cannot_reconcile_incomplete_or_untrusted_state(self):
        for state in (initial_state(), initial_state() | {"phase": "succeeded"}):
            with self.subTest(state=state), self.assertRaisesRegex(DeploymentError, "RECOVERY_REQUIRED"):
                self.store.reconcile(state)

    def test_failed_rename_preserves_last_valid_state_and_removes_temporary(self):
        target = self.root / "status.json"
        atomic_write(target, b"original")
        with patch("deploy_storage.os.replace", side_effect=OSError("disk")), self.assertRaises(OSError):
            atomic_write(target, b"new")
        self.assertEqual(b"original", private_read(target))
        self.assertEqual([target], list(self.root.iterdir()))

    def test_reconcile_repairs_crash_after_authoritative_commit(self):
        value = initial_state() | {"current": release(), "phase": "rolledBack"}
        self.store.save(value)
        atomic_write(self.root / "current.env", b"outdated")
        atomic_write(self.root / "in-progress.env", b"interrupted")
        self.store.reconcile(self.store.load())
        self.assertEqual(dotenv(release()).encode(), private_read(self.root / "current.env"))
        self.assertFalse((self.root / "in-progress.env").exists())

    def test_rejects_permissions_owner_size_and_special_files(self):
        target = self.root / "private"
        atomic_write(target, b"1234")
        with self.assertRaisesRegex(DeploymentError, "PRIVATE_FILE_TOO_LARGE"):
            private_read(target, 3)
        target.chmod(0o644)
        with self.assertRaisesRegex(DeploymentError, "UNSAFE_PRIVATE_FILE"):
            private_read(target)
        target.chmod(0o600)
        os.chown(target, 1654, 1654)
        with self.assertRaisesRegex(DeploymentError, "UNSAFE_PRIVATE_FILE"):
            private_read(target)
        target.unlink()
        os.mkfifo(target, 0o600)
        with self.assertRaisesRegex(DeploymentError, "UNSAFE_PRIVATE_FILE"):
            private_read(target)

    def test_invalid_json_is_never_silently_reset(self):
        atomic_write(self.root / "status.json", b"{")
        with self.assertRaises(json.JSONDecodeError):
            self.store.load()


if __name__ == "__main__":
    unittest.main()
