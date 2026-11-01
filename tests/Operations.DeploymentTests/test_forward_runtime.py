"""Check strict forward wiring and historical identity inspection without a provider."""

from datetime import timedelta
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

from test_forward_policy import pairs
from test_coordinated_policy import CONFIG, NOW, SNAPSHOT, backup
from coordinated_runtime import ForwardRuntime
from coordinated_cli import HistoricalSmoke
from deploy_policy import DeploymentError


class ForwardRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.marker = Path(self.directory.name) / "maintenance.json"
        self.backend, self.frontend, self.gate = Mock(), Mock(), Mock()
        self.backend.runtime.history.return_value = ["001_initial.sql"]
        self.old, self.new = pairs()
        self.capture = Mock(return_value="e" * 64)
        self.runtime = ForwardRuntime(self.backend, self.frontend, self.gate, lambda: CONFIG, lambda: "a" * 64, backup,
            NOW - timedelta(minutes=10), clock=lambda: NOW, backup_capture=self.capture, maintenance=self.marker)
        self.intent = {"version": 2, "phase": "prepared", "previous": self.old, "candidate": self.new,
                       "backup": SNAPSHOT, "frontendBackup": None, "toolDigest": "a" * 64}

    def test_preflight_requires_original_api_schema_and_pins_both_effects(self):
        self.runtime.preflight(self.old, self.new, SNAPSHOT)
        self.backend.verify_schema.assert_called_once_with(self.old)
        self.backend.prepare_forward.assert_called_once_with(self.old, self.new)
        self.frontend.prepare_forward.assert_called_once_with(self.old, self.new)
        self.runtime.validate_intent(self.intent)
        self.runtime.install_backend(self.new)
        self.assertEqual("e" * 64, self.runtime.capture_frontend_backup(self.new))
        self.runtime.install_frontend(self.new)
        self.runtime.verify_pair(self.new)
        self.backend.install_forward.assert_called_once_with(self.old, self.new)
        self.frontend.activate_forward.assert_called_once_with(self.old, self.new)
        self.backend.restore.assert_not_called()
        self.frontend.restore.assert_not_called()

    def test_interrupted_backup_is_recovered_before_backend_health_is_required(self):
        self.runtime.validate_intent(self.intent)
        self.marker.touch()
        with patch("coordinated_runtime.backup_command", side_effect=lambda *args, **kwargs: self.marker.unlink()) as recover:
            self.runtime.install_backend(self.new)
        recover.assert_called_once_with("recover", inherited=True)
        self.backend.install_forward.assert_called_once()
        self.marker.touch()
        with patch("coordinated_runtime.backup_command"), self.assertRaises(DeploymentError):
            self.runtime.install_backend(self.new)

    def test_unverified_backup_and_changed_schema_prevent_preparation(self):
        with self.assertRaises(DeploymentError):
            self.runtime.preflight(self.old, self.new, "0" * 64)
        self.backend.prepare_forward.assert_not_called()
        self.backend.verify_schema.side_effect = DeploymentError("BACKEND_CONTRACT_CHANGED")
        with self.assertRaises(DeploymentError):
            self.runtime.preflight(self.old, self.new, SNAPSHOT)
        self.backend.prepare_forward.assert_not_called()


class HistoricalIdentityTests(unittest.TestCase):
    def test_historical_reader_never_overrides_an_explicit_administrator_flag(self):
        smoke = HistoricalSmoke(Mock(), {"memberId": "expected"}, Path("unused"))
        smoke.verify_identity({"id": "expected", "isAdmin": False})
        smoke.verify_identity({"id": "expected", "roles": ["Member"]})
        for value in ({"id": "expected", "roles": ["Member"], "isAdmin": True},
                      {"id": "expected", "isAdmin": "false"}, {"id": "expected", "roles": []},
                      {"id": "foreign", "roles": ["Member"]}):
            with self.subTest(value=value), self.assertRaises(DeploymentError):
                smoke.verify_identity(value)


if __name__ == "__main__":
    unittest.main()
