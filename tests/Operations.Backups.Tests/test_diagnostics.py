"""Read-only diagnostics expose booleans and fixed codes, never subprocess text."""

import contextlib
import io
import json
import os
from pathlib import Path
import runpy
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

import diagnose_recovery
from fixtures import capture
from monkado_backup import operations


class DiagnosticTests(unittest.TestCase):
    def test_capture_check_handles_valid_missing_and_non_root_cases(self):
        # Arrange
        script = Path(__file__).resolve().parents[2] / "src/Operations.Backups/check_capture.py"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "pending").mkdir()
            capture(root / "pending")
            output = io.StringIO()
            # Act
            with patch.object(operations, "STATE", root), contextlib.redirect_stdout(output):
                runpy.run_path(str(script), run_name="__main__")
            # Assert
            values = [json.loads(line) for line in output.getvalue().splitlines()]
            self.assertFalse(values[0]["manifestValid"])
            self.assertTrue(values[1]["manifestValid"])
            self.assertFalse(values[2]["maintenancePending"])
        with patch.object(os, "geteuid", return_value=1000), self.assertRaises(SystemExit):
            runpy.run_path(str(script), run_name="__main__")
        runpy.run_path(str(script), run_name="not_main")

    def test_recovery_diagnostic_redacts_output_and_only_uses_dry_run(self):
        # Arrange
        def factory(run=None):
            value = Mock()
            value.compose.side_effect = (lambda *args: run(list(args))) if run else lambda *args: "container-id"
            return value
        output = io.StringIO()
        with patch.object(diagnose_recovery, "Operations", side_effect=factory), contextlib.redirect_stdout(output):
            for diagnostic in (b"secret-canary migration depend no container memory", b"not found oom", b"unknown"):
                with patch.object(diagnose_recovery.subprocess, "run", return_value=SimpleNamespace(
                    returncode=1, stdout=diagnostic, stderr=b"")) as process:
                    # Act
                    diagnose_recovery.main()
                    # Assert
                    self.assertIn("--dry-run", process.call_args.args[0])
        self.assertNotIn("secret-canary", output.getvalue())

    def test_non_root_and_command_errors_do_not_escape(self):
        # Arrange
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            # Act
            with patch.object(os, "geteuid", return_value=1000):
                diagnose_recovery.main()
            with patch.object(operations, "Operations", side_effect=RuntimeError("secret-canary")):
                runpy.run_path(diagnose_recovery.__file__, run_name="__main__")
            # Assert
        self.assertIn("ROOT_REQUIRED", output.getvalue())
        self.assertIn("DIAGNOSTIC_FAILED", output.getvalue())
        self.assertNotIn("secret-canary", output.getvalue())
