"""Check first-install wiring without touching host services or providers."""

from contextlib import nullcontext
from datetime import timedelta
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Native", "src/Operations.Deployment", "src/Operations.Frontend", "src/Operations.Backups",
             "deployments/production", "tests/Operations.DeploymentTests"):
    sys.path.insert(0, str(ROOT / path))
from coordinated_install_runtime import InstallationRuntime
from deploy_policy import DeploymentError
from test_coordinated_policy import CONFIG, NOW, SNAPSHOT, backup, pair


class InstallationRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.backend_store, self.native, self.publisher, self.gate = Mock(), Mock(), Mock(), Mock()
        self.gate.exists.return_value = False
        self.native.run.side_effect = lambda args, **_: b"disabled\n" if "UnitFileState" in args else b"inactive\n"
        self.approval, self.backup_capture = Mock(), Mock(return_value="f" * 64)
        self.runtime = InstallationRuntime(self.root, self.root, self.root, self.root, self.root, self.root,
                                           self.backend_store, self.native, self.publisher, self.gate,
                                           lambda: CONFIG, self.approval, backup, NOW - timedelta(minutes=10),
                                           clock=lambda: NOW, backup_capture=self.backup_capture,
                                           backup_state_directory=self.root)
        self.previous = pair("a", "e", "123-1")
        self.intent = {"manifest": {"reviewed": True}, "previousPair": self.previous,
                       "previousBackupFiles": {"old": "hash"}, "backupStarted": (NOW - timedelta(minutes=10)).isoformat()}

    def test_preflight_when_ready_checks_fresh_backup_and_current_pair(self):
        with patch.object(self.runtime, "_source"), patch.object(self.runtime, "_pair", return_value=self.previous), \
             patch("coordinated_install_runtime.backup_hashes", return_value={"old": "hash"}):
            result = self.runtime.preflight(self.intent["manifest"], SNAPSHOT)
        self.assertEqual(result["pair"], self.previous)
        self.assertEqual(result["backupFiles"], {"old": "hash"})
        self.approval.assert_called_once_with(self.intent["manifest"])

    def test_preflight_when_automatic_timer_is_enabled_refuses(self):
        self.native.run.side_effect = None
        self.native.run.return_value = b"enabled\n"
        with patch.object(self.runtime, "_source"), self.assertRaisesRegex(DeploymentError, "AUTOMATIC_PUBLICATION_ACTIVE"):
            self.runtime.preflight(self.intent["manifest"], SNAPSHOT)

    def test_backup_swap_uses_dedicated_lock_and_exact_original_hashes(self):
        with patch("coordinated_install_runtime.backup_lock", return_value=nullcontext()) as lock, \
             patch("coordinated_install_runtime.activate_backup") as activate:
            self.runtime.install_backup_code(self.intent)
        lock.assert_called_once_with(self.root / "backup.lock")
        activate.assert_called_once_with(self.root, self.intent["manifest"], self.root, self.intent["previousBackupFiles"])
        self.gate.verify.assert_called_once()

    def test_recovery_when_backup_maintenance_persists_does_not_continue(self):
        (self.root / "maintenance.json").touch()
        with patch("coordinated_install_runtime.backup_command") as recover, \
             self.assertRaisesRegex(DeploymentError, "BACKUP_RECOVERY_REQUIRED"):
            self.runtime.revalidate_recovery(self.intent)
        recover.assert_called_once_with("recover", inherited=True)

    def test_close_gate_when_unavailable_stops_caddy(self):
        self.gate.close.side_effect = ValueError("private error")
        with self.assertRaisesRegex(DeploymentError, "TRAFFIC_GATE_UNAVAILABLE"):
            self.runtime.close_gate()
        self.native.run.assert_called_once_with(["systemctl", "stop", "caddy.service"], timeout=120)

    def test_verify_pair_when_saved_frontend_marker_is_stale_uses_verified_current_pair(self):
        old_marker = {"revision": "c" * 40, "apiOrigin": "https://api.monkado.fr", "googleEnabled": True}
        baseline = {"infrastructure": {"caddy": {}, "postgres": {}}, "frontend": old_marker}
        self.backend_store.load.return_value = {"baseline": baseline}
        self.native.smoke.return_value = {"technical": True, "functional": True, "frontend": True}
        with patch.object(self.runtime, "_pair", return_value=self.previous):
            self.runtime.verify_active_pair(self.intent)
        restored = self.native.restore_baseline.call_args.args[0]
        self.assertEqual(restored["frontend"]["revision"], self.previous["frontend"]["revision"])
        self.assertEqual(restored["frontend"]["apiOrigin"], "https://api.monkado.fr")
        self.assertEqual(baseline["frontend"], old_marker)
        self.native.smoke.assert_called_once_with(self.previous["backend"])


if __name__ == "__main__":
    unittest.main()
