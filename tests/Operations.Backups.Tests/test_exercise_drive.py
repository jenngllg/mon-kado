"""Verify opt-in exercise orchestration using synthetic files and fake providers."""

import contextlib
import io
import json
import os
from pathlib import Path
import runpy
import shutil
import tempfile
import unittest
from unittest.mock import AsyncMock, Mock, patch

import exercise_drive
from monkado_backup.capture import digest
import test_postgres_integration


class ExerciseDriveTests(unittest.TestCase):
    def setUp(self):
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.root = Path(self.stack.enter_context(tempfile.TemporaryDirectory()))
        (self.root / "exercise").mkdir()
        (self.root / "fixture/keys").mkdir(parents=True)
        (self.root / "fixture/keys/key-test.xml").write_text("<key/>")
        self.row = {"protectedSecret": "synthetic-ciphertext"}
        (self.root / "fixture/row.json").write_text(json.dumps(self.row))
        self.stack.enter_context(patch.object(exercise_drive, "Path", side_effect=lambda value: self.root / value.lstrip("/")))
        self.stack.enter_context(patch.dict(os.environ, {"MONKADO_DRIVE_EXERCISE": "1"}))
        self.stack.enter_context(patch.object(test_postgres_integration, "wait_for_healthy", new=AsyncMock()))
        self.stack.enter_context(patch.object(exercise_drive, "private_file"))
        self.restore = Mock()
        self.restore.volumes.return_value = {"databaseStopped": True, "applicationStarted": False}
        self.stack.enter_context(patch.object(exercise_drive, "Restore", return_value=self.restore))
        self.process = self.stack.enter_context(patch.object(exercise_drive, "command", side_effect=self.command))
        self.output = io.StringIO()
        self.stack.enter_context(contextlib.redirect_stdout(self.output))
        self.stack.enter_context(contextlib.redirect_stderr(self.output))

    def command(self, arguments, **kwargs):
        """Only the documented synthetic repository and explicit Docker operations exist."""
        if arguments[0] == "restic":
            self.assertIn("rclone:monkado:MonKado-backups/exercise-813-20260921", arguments)
            self.assertNotIn("rclone:monkado:MonKado-backups/production-v1", arguments)
            if "backup" in arguments:
                return json.dumps({"message_type": "summary", "snapshot_id": "a" * 64, "data_added": 0})
            if "restore" in arguments:
                shutil.copytree(self.root / "exercise/original-not-used-for-restore", self.root / "exercise/restored")
            return ""
        if arguments[:3] == ["docker", "image", "inspect"]:
            return "postgres@sha256:" + "d" * 64
        if "output" in kwargs:
            kwargs["output"].write(b"synthetic-dump")
        if "SELECT administrator FROM backup_probe WHERE id=1" in arguments:
            return json.dumps(self.row)
        if "SELECT image_hash FROM backup_probe WHERE id=1" in arguments:
            return digest(next((self.root / "exercise/restored/images").rglob("*.webp")))
        if "sha256sum" in arguments:
            mount = arguments[arguments.index("--mount") + 1]
            folder = "keys" if "-keys," in mount else "images"
            return digest(self.root / "exercise/restored" / folder / arguments[-1].removeprefix("/restored/")) + " file"
        return ""

    def test_opted_in_exercise_checks_deduplication_restore_and_leaves_database_stopped(self):
        # Arrange / Act
        exercise_drive.main()
        # Assert
        result = json.loads((self.root / "exercise/result.json").read_text())
        self.assertTrue(result["databaseStopped"])
        self.assertTrue(result["deduplicationVerified"])
        self.assertTrue(result["hashesVerified"])
        self.restore.volumes.assert_called_once()
        self.assertEqual("stop", self.process.call_args.args[0][1])

    def test_existing_capture_is_not_overwritten(self):
        # Arrange / Act / Assert
        for folder in ("original", "restored"):
            path = self.root / "exercise" / folder
            path.mkdir()
            with self.subTest(folder=folder), self.assertRaisesRegex(RuntimeError, "EXERCISE_ALREADY_EXISTS"):
                exercise_drive.main()
            path.rmdir()
        self.process.assert_not_called()

    def test_no_opt_in_and_cli_failure_never_contact_drive(self):
        # Arrange / Act / Assert
        with patch.dict(os.environ, {"MONKADO_DRIVE_EXERCISE": "0"}):
            with self.assertRaisesRegex(RuntimeError, "EXPLICIT_OPT_IN_REQUIRED"):
                exercise_drive.main()
            with self.assertRaises(SystemExit) as result:
                runpy.run_path(exercise_drive.__file__, run_name="__main__")
        self.assertEqual(1, result.exception.code)
        self.process.assert_not_called()
