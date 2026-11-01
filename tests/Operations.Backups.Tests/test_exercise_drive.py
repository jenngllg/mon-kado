"""Verify native opt-in exercise orchestration with fake providers."""
import contextlib
import io
import json
import os
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch

import exercise_drive


class ExerciseDriveTests(unittest.TestCase):
    def setUp(self):
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.root = Path(self.stack.enter_context(tempfile.TemporaryDirectory()))
        self.binary = self.root / "migrate"
        self.binary.write_bytes(b"fixture")
        self.work = self.root / "exercise"
        self.work.mkdir()
        (self.work / "original").mkdir()
        self.stack.enter_context(patch.dict(os.environ, {"MONKADO_DRIVE_EXERCISE": "1", "MONKADO_NATIVE_MIGRATOR": str(self.binary)}))
        self.stack.enter_context(patch.object(exercise_drive.os, "geteuid", return_value=0))
        self.stack.enter_context(patch.object(exercise_drive.tempfile, "mkdtemp", return_value=str(self.work)))
        self.stack.enter_context(patch.object(exercise_drive, "private_file"))
        self.manifest = {"schemaVersion": 5}
        self.prepare = self.stack.enter_context(patch.object(exercise_drive, "prepare_capture", return_value=(self.manifest, {"protectedSecret": "opaque"}, "b" * 64)))
        self.stack.enter_context(patch.object(exercise_drive, "verify_manifest", return_value=self.manifest))
        self.verify = self.stack.enter_context(patch.object(exercise_drive, "verify_restoration", return_value={"databaseStopped": True, "applicationStarted": False}))
        self.process = self.stack.enter_context(patch.object(exercise_drive, "command", side_effect=self.command))
        self.output = io.StringIO()
        self.stack.enter_context(contextlib.redirect_stdout(self.output))
        self.stack.enter_context(contextlib.redirect_stderr(self.output))

    def command(self, arguments, **kwargs):
        self.assertEqual("restic", arguments[0])
        repository = arguments[arguments.index("--repo") + 1]
        self.assertRegex(repository, r"^rclone:monkado:MonKado-backups/exercise-native-[a-f0-9]{16}$")
        self.assertNotIn("production-v1", repository)
        if "backup" in arguments:
            return json.dumps({"message_type": "summary", "snapshot_id": "a" * 64, "data_added": 0})
        return ""

    def test_opted_in_exercise_checks_deduplication_restore_and_keeps_database_stopped(self):
        # Arrange / Act
        exercise_drive.main()
        # Assert
        result = json.loads((self.work / "result.json").read_text())
        self.assertTrue(result["databaseStopped"])
        self.assertTrue(result["deduplicationVerified"])
        self.assertTrue(result["hashesVerified"])
        self.assertFalse(result["applicationStarted"])
        self.prepare.assert_called_once_with(self.work, self.binary)
        self.verify.assert_called_once()
        calls = [call.args[0] for call in self.process.call_args_list]
        self.assertEqual(5, len(calls))
        self.assertTrue(any("check" in call and "--read-data" in call for call in calls))

    def test_nonzero_duplicate_data_blocks_restoration(self):
        # Arrange
        self.process.side_effect = lambda *args, **kwargs: json.dumps({"message_type": "summary", "snapshot_id": "a" * 64, "data_added": 1})
        # Act / Assert
        with self.assertRaisesRegex(RuntimeError, "DEDUPLICATION_FAILED"):
            exercise_drive.main()
        self.verify.assert_not_called()

    def test_non_root_operator_never_contacts_drive(self):
        # Arrange / Act / Assert
        with patch.object(exercise_drive.os, "geteuid", return_value=1000):
            with self.assertRaisesRegex(RuntimeError, "ROOT_REQUIRED"):
                exercise_drive.main()
        self.prepare.assert_not_called()
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
