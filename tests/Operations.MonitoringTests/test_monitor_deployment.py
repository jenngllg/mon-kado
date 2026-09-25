"""Deployment failures remain visible after rollback and disappear only after explicit resolution."""

from datetime import timedelta
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock

from monitor_deployment import collect, decisions
from monitor_storage import atomic_json
from test_monitor_policy import NOW


class DeploymentMonitorTests(unittest.TestCase):
    def test_current_terminal_and_interrupted_outcomes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            folder = root / "monkado-deployment"
            folder.mkdir()
            for phase, error, active, result, failed, recovery, rollback in (
                ("succeeded", None, "inactive", "success", False, False, False),
                ("rolledBack", "SMOKE_FAILED", "inactive", "success", True, False, False),
                ("acknowledged", None, "inactive", "success", False, False, False),
                ("succeeded", None, "failed", "exit-code", True, False, False),
                ("verifying", None, "activating", "success", False, False, False),
                ("verifying", None, "inactive", "success", False, True, False),
                ("recoveryRequired", "ROLLBACK_FAILED", "failed", "exit-code", True, True, True),
            ):
                state = {"schemaVersion": 1, "phase": phase, "error": error, "startedAt": NOW.isoformat()}
                atomic_json(folder / "status.json", state)
                runner = Mock(return_value=(0, f"ActiveState={active}\nResult={result}\n"))
                with self.subTest(phase=phase, active=active):
                    self.assertEqual({"failed": failed, "recoveryRequired": recovery, "rollbackFailed": rollback}, collect(root, runner, NOW))
            atomic_json(folder / "status.json", {"schemaVersion": 1, "phase": "migrating", "error": None,
                                                 "startedAt": (NOW - timedelta(seconds=901)).isoformat()})
            runner.return_value = (0, "ActiveState=activating\nResult=success\n")
            self.assertTrue(collect(root, runner, NOW)["recoveryRequired"])
            atomic_json(folder / "status.json", {})
            with self.assertRaises(ValueError):
                collect(root, runner, NOW)

    def test_unknown_cannot_clear_failure(self):
        self.assertIsNone(decisions(None)["deployment.failed"])
        self.assertTrue(decisions(None)["deployment.unavailable"])
        healthy = decisions({"failed": False, "recoveryRequired": False, "rollbackFailed": False})
        self.assertTrue(all(value is False for value in healthy.values()))


if __name__ == "__main__":
    unittest.main()
