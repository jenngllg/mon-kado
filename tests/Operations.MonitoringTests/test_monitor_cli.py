"""Exercise the fixed CLI without privileged host changes or external calls."""

from contextlib import redirect_stdout
from datetime import timedelta
import io
import json
from pathlib import Path
import runpy
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

import monitor_cli as cli
import monitor_runtime as runtime
from monitor_storage import atomic_json
from test_monitor_policy import NOW


class CliTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.configuration = self.root / "settings.json"
        atomic_json(self.configuration, {"schemaVersion": 1, "frontendEnabled": False,
                                         "notificationsEnabled": False, "thresholds": {}})
        self.report = {"schemaVersion": 1, "createdAt": NOW.isoformat(), "health": "healthy",
                       "openIncidents": [], "unknownChecks": [], "notificationError": None,
                       "notificationsEnabled": False}
        atomic_json(self.root / "status.json", self.report)

    def test_status_reports_staleness_and_durable_failure_without_reading_its_contents(self):
        # Arrange / Act / Assert
        result = cli.execute(["status"], NOW, self.root, self.configuration)
        self.assertEqual("healthy", result["health"])
        self.assertEqual("degraded", cli.status(self.root, NOW + timedelta(seconds=181))["health"])
        (self.root / "failure.json").write_text("PRIVATE_ERROR")
        result = cli.status(self.root, NOW)
        self.assertTrue(result["monitorFailed"])
        self.assertNotIn("PRIVATE", json.dumps(result))

    def test_status_rejects_unexpected_fields_and_sensitive_incident_strings(self):
        # Arrange / Act / Assert
        for value in (self.report | {"secret": "PRIVATE"}, self.report | {"openIncidents": ["PRIVATE@example.test"]}):
            atomic_json(self.root / "status.json", value)
            with self.assertRaises(ValueError):
                cli.status(self.root, NOW)

    def test_run_clears_failure_only_after_successful_cycle(self):
        # Arrange
        observer = Mock()
        observer.run.return_value = self.report
        (self.root / "failure.json").write_text("failure")
        # Act
        result = cli.execute(["run"], NOW, self.root, self.configuration, observer)
        # Assert
        self.assertEqual(self.report, result)
        self.assertFalse((self.root / "failure.json").exists())
        observer.run.assert_called_once()
        observer.reset_mock()
        with patch.object(runtime, "Monitor", return_value=observer) as factory:
            cli.execute(["run"], NOW, self.root, self.configuration)
        factory.assert_called_once_with(self.root)

    def test_check_config_does_not_print_settings_or_credentials(self):
        # Arrange / Act
        result = cli.execute(["check-config"], NOW, self.root, self.configuration)
        # Assert
        self.assertEqual({"configurationValid": True, "notificationsEnabled": False, "frontendEnabled": False}, result)
        with patch.object(cli.os, "geteuid", return_value=1000), self.assertRaises(ValueError):
            cli.execute(["run"], NOW, self.root, self.configuration)
        with self.assertRaises(ValueError):
            cli.execute(["unknown-PRIVATE"], NOW, self.root, self.configuration)

    def test_main_sanitizes_errors_and_records_failure_when_possible(self):
        # Arrange / Act / Assert
        for arguments in (["run"], ["PRIVATE_ARGUMENT"]):
            output = io.StringIO()
            with patch.object(cli, "execute", side_effect=ValueError("PRIVATE_ERROR")), \
                    patch.object(runtime, "STATE", self.root), redirect_stdout(output):
                self.assertEqual(1, cli.main(arguments))
            self.assertNotIn("PRIVATE", output.getvalue())
        self.assertTrue((self.root / "failure.json").exists())
        with patch.object(cli, "execute", side_effect=ValueError()), \
                patch.object(cli, "atomic_json", side_effect=OSError()), redirect_stdout(io.StringIO()):
            self.assertEqual(1, cli.main(["run"]))

    def test_main_and_script_entry_return_only_safe_json(self):
        # Arrange
        output = io.StringIO()
        with patch.object(cli, "execute", return_value={"safe": True}) as execute, \
                patch.object(sys, "argv", ["monitor", "status"]), redirect_stdout(output):
            # Act / Assert
            self.assertEqual(0, cli.main())
        self.assertEqual({"safe": True}, json.loads(output.getvalue()))
        self.assertEqual(["status"], execute.call_args.args[0])
        with patch.object(sys, "argv", ["monitor", "invalid"]), redirect_stdout(io.StringIO()), self.assertRaises(SystemExit) as stopped:
            runpy.run_path(cli.__file__, run_name="__main__")
        self.assertEqual(1, stopped.exception.code)
