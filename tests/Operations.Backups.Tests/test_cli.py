"""Command dispatch and sanitized failure output."""

import contextlib
import io
import runpy
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from monkado_backup import cli
from monkado_backup.policy import BackupError


class CliTests(unittest.TestCase):
    def setUp(self):
        self.operations = Mock(spec=["status", "recover", "capture", "transfer", "resume_capture", "check", "restic", "update"])
        self.operations.status.return_value = {"health": "degraded"}
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def test_dispatch_only_known_operations(self):
        # Arrange / Act / Assert
        with patch.object(cli, "lock", side_effect=lambda path: contextlib.nullcontext()):
            for action, expected in (("backup", ["capture", "transfer"]), ("transfer", ["transfer"]),
                                     ("resume", ["resume_capture"]), ("check", ["check"]), ("initialize", ["restic"])):
                self.operations.reset_mock()
                cli.execute(action, self.operations)
                self.assertEqual(expected, [call[0] for call in self.operations.mock_calls])
            with self.assertRaises(BackupError):
                cli.execute("unknown", self.operations)
        with contextlib.redirect_stdout(io.StringIO()) as output:
            cli.execute("status", self.operations)
        self.assertIn("degraded", output.getvalue())

    def test_resume_without_capture_reports_explicit_noop(self):
        # Arrange
        self.operations.resume_capture.return_value = False
        # Act
        with patch.object(cli, "lock", return_value=contextlib.nullcontext()), contextlib.redirect_stdout(io.StringIO()) as output:
            cli.execute("resume", self.operations)
        # Assert
        self.assertIn("NO_PENDING_CAPTURE", output.getvalue())
        self.assertEqual(["resume_capture"], [call[0] for call in self.operations.mock_calls])

    def test_recover_only_locks_when_maintenance_marker_exists(self):
        # Arrange / Act / Assert
        with patch.object(cli, "STATE", self.root), patch.object(cli, "lock", side_effect=lambda path: contextlib.nullcontext()) as lock:
            cli.execute("recover", self.operations)
            lock.assert_not_called()
            (self.root / "maintenance.json").write_text("{}")
            cli.execute("recover", self.operations)
            self.assertEqual([cli.DEPLOYMENT / "backup-coordination.lock", cli.DEPLOYMENT / "deploy.lock"],
                             [entry.args[0] for entry in lock.call_args_list])
            self.operations.recover.assert_called_once()
        with patch.object(cli, "install_password") as install:
            cli.execute("credentials", self.operations)
            install.assert_called_once()

    def test_main_dispatches_restore_and_download(self):
        # Arrange / Act / Assert
        with patch.object(cli, "Operations", return_value=self.operations), patch.object(cli.os, "geteuid", return_value=0), patch.object(cli, "Restore") as restore:
            restore.return_value.volumes.return_value = {"applicationStarted": False}
            for action in ("download", "restore"):
                with patch.object(cli.sys, "argv", ["tool", action, "first", "second"]), contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(0, cli.main())
            restore.return_value.download.assert_called_once_with("first", "second")
            restore.return_value.volumes.assert_called_once_with("first", "second")
            with patch.object(cli.sys, "argv", ["tool", "status"]), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, cli.main())

    def test_errors_never_print_raw_exceptions(self):
        # Arrange / Act / Assert
        with patch.object(cli, "Operations", return_value=self.operations), patch.object(cli.os, "geteuid", return_value=0):
            for arguments in (["tool"], ["tool", "a", "b"]):
                with patch.object(cli.sys, "argv", arguments), contextlib.redirect_stderr(io.StringIO()) as output:
                    self.assertEqual(1, cli.main())
                    self.assertIn("INVALID_ARGUMENTS", output.getvalue())
            self.operations.status.side_effect = OSError("/private secret@example.test")
            self.operations.update.side_effect = OSError("second private secret")
            with patch.object(cli.sys, "argv", ["tool", "status"]), contextlib.redirect_stderr(io.StringIO()) as output:
                self.assertEqual(1, cli.main())
                self.assertNotIn("private", output.getvalue())
                self.assertIn("OPERATION_FAILED", output.getvalue())

    def test_module_entry_rejects_unprivileged_caller(self):
        # Arrange / Act / Assert
        with patch("os.geteuid", return_value=1000), patch("monkado_backup.operations.Operations", return_value=self.operations), contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as raised:
            runpy.run_module("monkado_backup.cli", run_name="__main__")
        self.assertEqual(1, raised.exception.code)
