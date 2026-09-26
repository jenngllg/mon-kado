"""Safe restore contracts with no real Docker or Google requests."""

import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock

from fixtures import capture
from monkado_backup.policy import BackupError
from monkado_backup.restore import Restore


class RestoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.operations = Mock(spec=["run", "restic", "inspect", "wait"])
        self.operations.run.side_effect = AssertionError("unexpected command")
        self.restore = Restore(self.operations)

    def test_download_verifies_and_refuses_existing_directory(self):
        # Arrange
        target = self.root / "download"
        self.operations.restic.side_effect = lambda *args: capture(target)
        # Act
        result = self.restore.download("a" * 64, target)
        # Assert
        self.assertEqual(4, result["schemaVersion"])
        self.operations.restic.assert_called_once_with("restore", "a" * 64, "--target", str(target))
        with self.assertRaisesRegex(BackupError, "RESTORE_TARGET_NOT_EMPTY"):
            self.restore.download("a" * 64, target)
        self.operations.run.assert_not_called()

    def test_invalid_project_cannot_touch_production(self):
        # Arrange / Act / Assert
        for name in ("mon-kado", "monkado-restore-x", "../production"):
            with self.subTest(name=name), self.assertRaises(BackupError):
                self.restore.volumes(self.root, name)
        self.operations.run.assert_not_called()

    def test_existing_resources_are_never_overwritten(self):
        # Arrange
        capture(self.root)
        for index in range(3):
            self.operations.run.side_effect = ["monkado-restore-12345678-postgres" if index == 0 else "",
                                               "monkado-restore-12345678" if index == 1 else "",
                                               "monkado-restore-12345678-postgres" if index == 2 else ""]
            # Act / Assert
            with self.subTest(index=index), self.assertRaisesRegex(BackupError, "RESTORE_RESOURCES_EXIST"):
                self.restore.volumes(self.root, "monkado-restore-12345678")

    def test_success_uses_fresh_network_no_ports_no_worker_and_stops_database(self):
        # Arrange
        capture(self.root)
        commands = []
        def run(args, **kwargs):
            commands.append(args)
            if args[1] == "create":
                return "temporary-helper"
            if args[1:3] == ["exec", "-i"]:
                self.assertEqual(b"PGDMP fixture", kwargs["input_file"].read())
            return ""
        self.operations.run.side_effect = run
        self.operations.inspect.side_effect = ["starting", "healthy"]
        # Act
        result = self.restore.volumes(self.root, "monkado-restore-12345678")
        # Assert
        self.assertTrue(result["databaseStopped"])
        self.assertFalse(result["applicationStarted"])
        self.assertTrue(any("--internal" in args for args in commands))
        self.assertFalse(any("-p" in args or "--publish" in args for args in commands))
        self.assertEqual(["docker", "stop", "monkado-restore-12345678-postgres"], commands[-1])
        self.operations.wait.assert_called_once_with(1)
        self.operations.restic.assert_not_called()

    def test_database_timeout_never_imports_dump(self):
        # Arrange
        capture(self.root)
        self.operations.run.side_effect = None
        self.operations.run.return_value = ""
        self.operations.inspect.return_value = "starting"
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "RESTORE_DATABASE_NOT_READY"):
            self.restore.volumes(self.root, "monkado-restore-12345678")
        self.assertFalse(any(call.args[0][1] == "exec" for call in self.operations.run.call_args_list))
        self.assertEqual(60, self.operations.wait.call_count)
        self.assertEqual(["docker", "stop", "monkado-restore-12345678-postgres"],
                         self.operations.run.call_args.args[0])
