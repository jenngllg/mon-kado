import contextlib
import io
import runpy
import tempfile
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

from performance_cli import main
from policy import PerformanceError


class CliTests(unittest.TestCase):
    def invoke(self, arguments, bench):
        bench.failure_diagnostics.return_value = {}
        bench.cg_created = False
        output = io.StringIO()
        with patch("sys.argv", ["performance_cli", *arguments]), patch("performance_cli.Bench", return_value=bench):
            with contextlib.redirect_stdout(output):
                code = main()
        return code, output.getvalue()

    def test_run_and_cleanup_outcomes_are_explicit(self):
        bench = Mock()
        bench.run.return_value = {"verdict": "passed"}
        self.assertEqual(0, self.invoke(["run"], bench)[0])
        bench.create.assert_called_once()
        bench.run.return_value = {"verdict": "failed"}
        self.assertEqual(1, self.invoke(["run"], bench)[0])
        self.assertEqual(0, self.invoke(["cleanup", "--run-id", "mk816-012345abcdef"], bench)[0])
        bench.cleanup.assert_called_once()
        bench.stage = "preflight"
        self.assertIn("EXPLICIT_RUN_REQUIRED", self.invoke(["cleanup"], bench)[1])

    def test_faults_never_print_original_exception_text(self):
        bench = Mock(stage="seed")
        for error in (OSError("secret"), ValueError("secret"), KeyboardInterrupt()):
            bench.create.side_effect = error
            code, output = self.invoke(["run"], bench)
            self.assertEqual(1, code)
            self.assertNotIn("secret", output)
        bench.create.side_effect = PerformanceError("OWNERSHIP_MISMATCH")
        self.assertIn("OWNERSHIP_MISMATCH", self.invoke(["run"], bench)[1])

    def test_script_entrypoint_preserves_exit_status(self):
        bench = Mock()
        bench.run.return_value = {"verdict": "passed"}
        with patch("sys.argv", ["performance_cli", "run"]), patch("runtime.Bench", return_value=bench):
            with contextlib.redirect_stdout(io.StringIO()), self.assertRaises(SystemExit) as exited:
                runpy.run_module("performance_cli", run_name="__main__")
        self.assertEqual(0, exited.exception.code)

    def test_startup_failure_writes_only_sanitized_diagnostic_artifact(self):
        with tempfile.TemporaryDirectory() as root:
            bench = Mock(cg_created=True, directory=Path(root), stage="services")
            bench.create.side_effect = PerformanceError("READINESS_TIMEOUT")
            bench.failure_diagnostics.return_value = {"api": {"oom": True}}
            with patch("sys.argv", ["performance_cli", "run"]), patch("performance_cli.Bench", return_value=bench):
                with contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(1, main())
                    self.assertEqual(1, main())
            self.assertIn("READINESS_TIMEOUT", (Path(root) / "report.json").read_text())
