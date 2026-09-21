"""Private atomic state and bounded retention tests in disposable directories."""

from datetime import timedelta
from pathlib import Path
import tempfile
import unittest

import monitor_storage as storage
from test_monitor_policy import NOW, snapshot


class StorageTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def test_atomic_write_is_private_and_replaces_previous_state(self):
        # Arrange
        path = self.root / "state.json"
        storage.atomic_json(path, {"version": 1})
        # Act
        storage.atomic_json(path, {"version": 2})
        # Assert
        self.assertEqual({"version": 2}, storage.read_json(path, private=True))
        self.assertEqual(0o600, path.stat().st_mode & 0o777)
        self.assertFalse(path.with_suffix(".new").exists())
        path.chmod(0o644)
        with self.assertRaises(ValueError):
            storage.read_json(path, private=True)

    def test_symlink_and_oversized_files_are_rejected(self):
        # Arrange
        target = self.root / "target"
        target.write_text("{}")
        link = self.root / "state.json"
        link.symlink_to(target)
        # Act / Assert
        with self.assertRaises(ValueError):
            storage.read_json(link)
        with self.assertRaises(ValueError):
            storage.atomic_json(link, {})
        with self.assertRaises(ValueError):
            storage.atomic_json(target, {"data": "x" * storage.MAX_JSON_BYTES})
        target.write_bytes(b"x" * (storage.MAX_JSON_BYTES + 1))
        with self.assertRaises(ValueError):
            storage.read_json(target)

    def test_retention_removes_only_owned_old_samples_and_interrupted_writes(self):
        # Arrange
        history = self.root / "history"
        storage.append_history(history, NOW - timedelta(days=8), {"api": None})
        boundary = NOW - timedelta(days=7)
        storage.append_history(history, boundary, {"api": None})
        leftover = history / "20260920T1200Z.new"
        leftover.write_text("partial")
        unrelated = history / "keep.txt"
        unrelated.write_text("unchanged")
        # Act
        storage.append_history(history, NOW, {"api": snapshot()})
        # Assert
        self.assertEqual(2, len(list(history.glob("*.json"))))
        self.assertTrue((history / boundary.strftime("%Y%m%dT%H%MZ.json")).exists())
        self.assertFalse(leftover.exists())
        self.assertEqual("unchanged", unrelated.read_text())

    def test_size_limit_reserves_space_before_writing_new_sample(self):
        # Arrange
        history = self.root / "history"
        storage.append_history(history, NOW, {"sample": 1}, maximum_bytes=20)
        # Act
        storage.append_history(history, NOW + timedelta(minutes=1), {"sample": 2}, maximum_bytes=20)
        # Assert
        self.assertEqual(1, len(list(history.glob("*.json"))))
        self.assertLessEqual(sum(item.stat().st_size for item in history.iterdir()), 20)
        with self.assertRaises(ValueError):
            storage.append_history(history, NOW, {"sample": "oversized"}, maximum_bytes=1)

    def test_recent_history_ignores_null_api_and_missing_files(self):
        # Arrange
        history = self.root / "history"
        self.assertEqual([], storage.recent_snapshots(history, NOW))
        expected = snapshot(when=NOW - timedelta(minutes=1))
        storage.append_history(history, NOW - timedelta(minutes=1), {"api": expected})
        storage.append_history(history, NOW - timedelta(minutes=2), {"api": None})
        # Act / Assert
        self.assertEqual([expected], storage.recent_snapshots(history, NOW))

    def test_unknown_json_name_is_not_deleted(self):
        # Arrange
        history = self.root / "history"
        history.mkdir()
        unknown = history / "unknown.json"
        unknown.write_text("{}")
        # Act / Assert
        with self.assertRaises(ValueError):
            storage.append_history(history, NOW, {})
        self.assertTrue(unknown.exists())
