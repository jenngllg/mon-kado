"""Native restore contracts use private fresh resources and no production configuration."""

import shutil
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock

from fixtures import native_capture as capture
from monkado_backup.policy import BackupError
from monkado_backup.restore import Restore


class RestoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.capture = self.root / "capture"
        self.capture.mkdir(mode=0o700)
        self.operations = Mock(spec=["run", "restic"])
        self.operations.run.side_effect = AssertionError("unexpected command")
        self.restore = Restore(self.operations, root=self.root / "restores")

    def test_download_verifies_and_refuses_existing_directory(self):
        target = self.root / "download"
        self.operations.restic.side_effect = lambda *args: capture(target)
        result = self.restore.download("a" * 64, target)
        self.assertEqual(5, result["schemaVersion"])
        self.operations.restic.assert_called_once_with("restore", "a" * 64, "--target", str(target))
        with self.assertRaisesRegex(BackupError, "RESTORE_TARGET_NOT_EMPTY"):
            self.restore.download("a" * 64, target)
        self.operations.run.assert_not_called()

    def test_invalid_project_cannot_touch_production(self):
        for name in ("mon-kado", "monkado-restore-x", "../production"):
            with self.subTest(name=name), self.assertRaises(BackupError):
                self.restore.volumes(self.capture, name)
        self.operations.run.assert_not_called()

    def test_existing_resources_are_never_overwritten(self):
        capture(self.capture)
        self.restore.root.mkdir()
        target = self.restore.root / "monkado-restore-12345678"
        target.mkdir()
        with self.assertRaisesRegex(BackupError, "RESTORE_RESOURCES_EXIST"):
            self.restore.volumes(self.capture, target.name)
        self.operations.run.assert_not_called()

    def runner(self, args, **kwargs):
        if args[:2] == ["install", "-d"]:
            for directory in args[-2:]:
                Path(directory).mkdir(mode=0o700)
        if "--exit-on-error" in args:
            self.assertEqual(b"PGDMP fixture", kwargs["input_file"].read())
        if any(argument.endswith("/psql") for argument in args):
            return "001_baseline.sql\n"
        return ""

    def test_success_uses_new_private_socket_no_tcp_no_worker_and_stops_database(self):
        capture(self.capture)
        self.operations.run.side_effect = self.runner
        result = self.restore.volumes(self.capture, "monkado-restore-12345678")
        self.assertTrue(result["databaseStopped"])
        self.assertFalse(result["applicationStarted"])
        calls = [call.args[0] for call in self.operations.run.call_args_list]
        self.assertTrue(any("--options=-c listen_addresses=''" in str(args) for args in calls))
        self.assertTrue(any("--auth-host=reject" in args for args in calls))
        self.assertFalse(any("mon_kado" in args for args in calls))
        self.assertIn("stop", calls[-1])
        self.assertIn("--mode=fast", calls[-1])
        self.assertFalse(any("JWT_SIGNING_KEY" in str(args) or "DATABASE_URL" in str(args) for args in calls))
        self.operations.restic.assert_not_called()
        restored = self.restore.root / result["project"]
        self.assertTrue((restored / "images").is_dir())
        self.assertEqual(0o600, (restored / "configuration/production.env").stat().st_mode & 0o777)

    def test_database_start_failure_never_imports_dump_and_still_attempts_stop(self):
        capture(self.capture)
        def failure(args, **kwargs):
            if "start" in args:
                raise BackupError("RESTORE_DATABASE_NOT_READY")
            return self.runner(args, **kwargs)
        self.operations.run.side_effect = failure
        with self.assertRaisesRegex(BackupError, "RESTORE_DATABASE_NOT_READY"):
            self.restore.volumes(self.capture, "monkado-restore-12345678")
        calls = [call.args[0] for call in self.operations.run.call_args_list]
        self.assertFalse(any("--exit-on-error" in args for args in calls))
        self.assertIn("stop", calls[-1])

    def test_restored_history_mismatch_is_not_accepted_and_keeps_database_stopped(self):
        capture(self.capture)
        def failure(args, **kwargs):
            value = self.runner(args, **kwargs)
            return "wrong_history\n" if any(argument.endswith("/psql") for argument in args) else value
        self.operations.run.side_effect = failure
        with self.assertRaisesRegex(BackupError, "RESTORED_HISTORY_DIVERGED"):
            self.restore.volumes(self.capture, "monkado-restore-12345678")
        self.assertIn("stop", self.operations.run.call_args.args[0])
        self.assertFalse((self.restore.root / "monkado-restore-12345678/configuration").exists())
