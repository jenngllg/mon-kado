"""Deployment failures remain visible after rollback and disappear only after explicit resolution."""

from datetime import timedelta
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock

from monitor_deployment import collect, decisions, collect_frontend, frontend_decisions
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

    def test_frontend_pause_rollback_and_interrupted_outcomes(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            folder = root / "monkado-frontend"
            folder.mkdir()
            cases = [("healthy", None, "inactive", 0, False, False, False),
                     ("rolledBack", "PUBLICATION_FAILED", "inactive", 0, True, False, False),
                     ("recoveryRequired", "ROLLBACK_FAILED", "failed", 0, True, True, True),
                     ("activating", None, "activating", 0, False, False, False),
                     ("preparing", None, "inactive", 0, False, True, False),
                     ("recovering", None, "active", 601, False, True, False)]
            # Act / Assert
            for phase, error, active, seconds, failed, recovery, rollback in cases:
                state = {"schemaVersion": 2, "phase": phase, "error": error, "paused": True,
                         "updatedAt": (NOW - timedelta(seconds=seconds)).isoformat()}
                atomic_json(folder / "status.json", state)
                runner = Mock(return_value=(0, f"ActiveState={active}\nResult=success\n"))
                with self.subTest(phase=phase):
                    self.assertEqual({"failed": failed, "recoveryRequired": recovery, "rollbackFailed": rollback},
                                     collect_frontend(root, runner, NOW))
            for change in ({"schemaVersion": 1}, {"phase": "legacy"}, {"phase": "idle"}, {"phase": "bad"},
                           {"paused": "yes"}, {"error": "private-canary"}):
                atomic_json(folder / "status.json", state | change)
                with self.assertRaises(ValueError):
                    collect_frontend(root, runner, NOW)
            atomic_json(folder / "status.json", state)
            runner.return_value = (1, "")
            with self.assertRaises(ValueError):
                collect_frontend(root, runner, NOW)
            self.assertIsNone(frontend_decisions(None)["frontend.deployment.failed"])

    def test_frontend_service_failure_cannot_be_hidden_by_old_healthy_state(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "monkado-frontend").mkdir()
            atomic_json(root / "monkado-frontend/status.json",
                        {"schemaVersion": 2, "phase": "healthy", "error": None, "paused": False})
            runner = Mock(return_value=(0, "ActiveState=failed\nResult=exit-code\n"))
            # Act / Assert
            self.assertTrue(collect_frontend(root, runner, NOW)["failed"])


if __name__ == "__main__":
    unittest.main()
