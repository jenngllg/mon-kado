"""Host adapters tested with strict subprocess boundaries and temporary storage."""

import json
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import Mock, patch

from fixtures import capture
from monkado_backup.operations import Operations
from monkado_backup.policy import BackupError, TAG


class OperationsTests(unittest.TestCase):
    def test_interrupted_promotion_preserves_previous_capture(self):
        # Arrange
        previous = self.root / "pending.previous"
        previous.mkdir()
        manifest = capture(previous)
        # Act
        self.operations.recover_capture()
        # Assert
        self.assertFalse(previous.exists())
        self.assertEqual(manifest, json.loads((self.root / "pending/manifest.json").read_text()))

    def test_integrity_check_does_not_clear_an_upload_error(self):
        # Arrange
        self.operations.update(error="REMOTE_BACKUP_FAILED")
        self.operations.restic = Mock(return_value="")
        # Act
        self.operations.check()
        # Assert
        self.assertEqual("REMOTE_BACKUP_FAILED", self.operations.state()["error"])
        self.operations.restic.assert_called_once_with("check", "--read-data")

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.patch = patch("monkado_backup.operations.STATE", self.root)
        self.patch.start()
        self.addCleanup(self.patch.stop)
        self.now = datetime(2026, 9, 21, tzinfo=timezone.utc)
        self.run = Mock(side_effect=AssertionError("unexpected command"))
        self.wait = Mock(side_effect=AssertionError("unexpected wait"))
        self.operations = Operations(self.run, lambda: self.now, self.wait)

    def test_state_and_status_are_private_metadata_only(self):
        # Arrange / Act
        self.operations.update(lastRemoteCapture=self.now.isoformat(), error=None)
        # Assert
        self.assertEqual("healthy", self.operations.status()["health"])
        self.run.assert_not_called()
        self.wait.assert_not_called()

    def test_recovery_without_marker_has_no_effect(self):
        # Arrange / Act
        self.operations.recover()
        # Assert
        self.run.assert_not_called()

    def test_recovery_rejects_arbitrary_services(self):
        # Arrange
        (self.root / "maintenance.json").write_text('{"services":["other"]}')
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "INVALID_MAINTENANCE"):
            self.operations.recover()
        self.run.assert_not_called()

    def test_cleanup_cannot_target_unrelated_directory(self):
        # Arrange / Act / Assert
        with self.assertRaisesRegex(BackupError, "UNSAFE_CLEANUP"):
            self.operations.remove_capture(self.root)
        self.assertTrue(self.root.exists())

    def test_failed_upload_retries_without_capture_or_prune(self):
        # Arrange
        pending = self.root / "pending"
        pending.mkdir()
        capture(pending)
        self.operations.restic = Mock(side_effect=BackupError("REMOTE_UNAVAILABLE"))
        self.run.side_effect = None
        self.run.return_value = '{"free":1099511627776}'
        self.wait.side_effect = None
        # Act / Assert
        with patch("monkado_backup.operations.private_file"), self.assertRaisesRegex(BackupError, "REMOTE_BACKUP_FAILED"):
            self.operations.transfer()
        self.assertEqual(3, self.operations.restic.call_count)
        self.assertTrue(all(call.args[0] == "backup" for call in self.operations.restic.call_args_list))
        self.assertEqual([((900,), {}), ((900,), {})], self.wait.call_args_list)
        self.assertTrue(pending.exists())
        self.assertNotIn("lastRemoteSuccess", self.operations.state())

    def test_quota_failure_does_not_upload_or_prune(self):
        # Arrange
        pending = self.root / "pending"
        pending.mkdir()
        capture(pending)
        self.operations.restic = Mock(side_effect=AssertionError("unexpected Restic"))
        self.run.side_effect = None
        self.run.return_value = '{"free":1}'
        self.wait.side_effect = None
        # Act / Assert
        with patch("monkado_backup.operations.private_file"), self.assertRaises(BackupError):
            self.operations.transfer()
        self.operations.restic.assert_not_called()
        self.assertEqual("INSUFFICIENT_REMOTE_QUOTA", self.operations.state()["error"])

    def test_verified_upload_removes_only_expired_own_snapshot(self):
        # Arrange
        pending = self.root / "pending"
        pending.mkdir()
        manifest = capture(pending)
        identifier = "a" * 64
        calls = []
        def restic(*args, **kwargs):
            calls.append(args)
            if args[0] == "backup":
                self.assertEqual(pending, kwargs["cwd"])
                return json.dumps({"message_type": "summary", "snapshot_id": identifier})
            if args[0] == "dump":
                return json.dumps(manifest)
            if args[0] == "snapshots":
                return json.dumps([{"id": identifier, "tags": [TAG], "time": self.now.isoformat()},
                                   {"id": "b" * 64, "tags": [TAG], "time": "2026-01-01T00:00:00Z"}])
            if args[0] in ("check", "forget", "prune"):
                return ""
            raise AssertionError("unexpected Restic command")
        self.operations.restic = restic
        self.run.side_effect = None
        self.run.return_value = '{"free":1099511627776}'
        # Act
        with patch("monkado_backup.operations.private_file"):
            self.operations.transfer()
        # Assert
        self.assertIn(("forget", "b" * 64), calls)
        self.assertFalse(pending.exists())
        self.assertEqual(identifier, self.operations.state()["snapshotId"])
        self.wait.assert_not_called()

    def test_mismatched_remote_manifest_preserves_local_capture(self):
        # Arrange
        pending = self.root / "pending"
        pending.mkdir()
        capture(pending)
        self.run.side_effect = None
        self.run.return_value = '{"free":1099511627776}'
        self.wait.side_effect = None
        def restic(*args, **kwargs):
            if args[0] == "backup":
                return json.dumps({"message_type": "summary", "snapshot_id": "a" * 64})
            if args[0] == "check":
                return ""
            if args[0] == "dump":
                return "{}"
            raise AssertionError("Pruning is forbidden after failed verification")
        self.operations.restic = restic
        # Act / Assert
        with patch("monkado_backup.operations.private_file"), self.assertRaises(BackupError):
            self.operations.transfer()
        self.assertTrue(pending.exists())
        self.assertNotIn("lastRemoteSuccess", self.operations.state())

    def test_upload_without_expired_snapshots_never_prunes(self):
        # Arrange
        pending = self.root / "pending"
        pending.mkdir()
        manifest = capture(pending)
        self.run.side_effect = None
        self.run.return_value = '{"free":1099511627776}'
        def restic(*args, **kwargs):
            outputs = {"backup": json.dumps({"message_type": "summary", "snapshot_id": "a" * 64}),
                       "check": "", "dump": json.dumps(manifest),
                       "snapshots": json.dumps([{"id": "a" * 64, "tags": [TAG], "time": self.now.isoformat()}])}
            return outputs[args[0]]
        self.operations.restic = restic
        # Act
        with patch("monkado_backup.operations.private_file"):
            self.operations.transfer()
        # Assert
        self.assertFalse(pending.exists())
        self.wait.assert_not_called()
