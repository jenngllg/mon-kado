"""Test the paired runtime boundary with simulated providers and real policy checks."""

from datetime import timedelta
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Deployment", "src/Operations.Frontend", "deployments/production",
             "tests/Operations.DeploymentTests"):
    sys.path.insert(0, str(ROOT / path))
from coordinated_runtime import CoordinatedRuntime
from deploy_policy import DeploymentError
from test_coordinated_policy import CONFIG, NOW, SNAPSHOT, backup, candidate_pair, pair


class CoordinatedRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.maintenance = Path(self.temporary.name) / "maintenance.json"
        self.previous, self.candidate = pair("a", "e", "123-1"), candidate_pair()
        self.backend, self.frontend, self.gate = Mock(), Mock(), Mock()
        self.backend.runtime.history.return_value = ["001_initial.sql"]
        self.capture = Mock(return_value="e" * 64)
        self.runtime = CoordinatedRuntime(self.backend, self.frontend, self.gate, lambda: CONFIG, lambda: "a" * 64, backup,
                                          NOW - timedelta(minutes=10), clock=lambda: NOW,
                                          backup_capture=self.capture, maintenance=self.maintenance)
        self.intent = {"version": 1, "phase": "prepared", "previous": self.previous,
                       "candidate": self.candidate, "backup": SNAPSHOT, "frontendBackup": None, "toolDigest": "a" * 64}

    def test_preflight_when_backup_and_pairs_are_valid_prepares_both(self):
        self.runtime.preflight(self.previous, self.candidate, SNAPSHOT)
        self.backend.prepare.assert_called_once_with(self.previous, self.candidate)
        self.frontend.prepare.assert_called_once_with(self.previous, self.candidate)

    def test_preflight_when_snapshot_is_wrong_does_not_prepare(self):
        with self.assertRaises(DeploymentError):
            self.runtime.preflight(self.previous, self.candidate, "f" * 64)
        self.backend.prepare.assert_not_called()
        self.frontend.prepare.assert_not_called()

    def test_runtime_when_intent_valid_uses_exact_pair_and_second_backup(self):
        self.runtime.validate_intent(self.intent)
        self.runtime.install_backend(self.candidate)
        self.assertEqual(self.runtime.capture_frontend_backup(self.candidate), "e" * 64)
        self.runtime.install_frontend(self.candidate)
        self.runtime.verify_pair(self.candidate)
        self.runtime.restore_backend(self.previous)
        self.runtime.restore_frontend(self.previous)
        self.backend.install.assert_called_once_with(self.previous, self.candidate)
        self.frontend.activate.assert_called_once_with(self.previous, self.candidate)
        self.frontend.verify.assert_called_once_with(self.candidate)
        self.backend.verify.assert_called_once_with(self.candidate)
        self.assertEqual(self.backend.verify_contract.call_count, 2)
        self.backend.restore.assert_called_once_with(self.previous, self.candidate)
        self.frontend.restore.assert_called_once_with(self.previous, self.candidate)
        self.assertTrue(self.capture.call_args.kwargs["inherited"])

    def test_restore_when_backup_maintenance_remains_refuses(self):
        self.runtime.validate_intent(self.intent)
        self.maintenance.touch()
        with patch("coordinated_runtime.backup_command") as recover:
            with self.assertRaisesRegex(DeploymentError, "BACKUP_RECOVERY_REQUIRED"):
                self.runtime.restore_backend(self.previous)
        recover.assert_called_once_with("recover", inherited=True)
        self.backend.restore.assert_not_called()

    def test_close_gate_when_nft_fails_stops_caddy_and_reports_failure(self):
        self.gate.close.side_effect = ValueError("private diagnostic")
        with self.assertRaisesRegex(DeploymentError, "TRAFFIC_GATE_UNAVAILABLE"):
            self.runtime.close_gate()
        self.backend.runtime.run.assert_called_once_with(["systemctl", "stop", "caddy.service"], timeout=120)


if __name__ == "__main__":
    unittest.main()
