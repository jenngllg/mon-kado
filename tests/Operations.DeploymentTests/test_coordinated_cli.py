"""Check production entrypoint ordering without invoking real services."""

from contextlib import contextmanager
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Native", "src/Operations.Deployment", "src/Operations.Frontend", "deployments/production"):
    sys.path.insert(0, str(ROOT / path))
import coordinated_cli as cli
from deploy_policy import DeploymentError


class CoordinatedCliTests(unittest.TestCase):
    def setUp(self):
        self.events = []
        self.store = Mock(); self.store.load.return_value = None
        self.gate = Mock(); self.gate.exists.return_value = False
        self.engine = Mock(); self.engine.deploy.return_value = {"backendSnapshot": "a" * 64, "frontendSnapshot": "b" * 64}
        self.engine.recover.return_value = "rolledBack"
        self.runtime, self.publisher, self.backend_store = Mock(), Mock(), Mock()

    @contextmanager
    def locked(self, _state):
        self.events.append("lock")
        try:
            yield
        finally:
            self.events.append("unlock")

    def test_deploy_when_ready_takes_backup_before_lock_and_transition(self):
        with patch.object(cli, "installed_tool", return_value=({"reviewed": True}, "digest")), \
             patch.object(cli, "require_protected_tool", side_effect=lambda _: self.events.append("approval")), \
             patch.object(cli, "CoordinatedStore", return_value=self.store), patch.object(cli, "Gate", return_value=self.gate), \
             patch.object(cli, "capture", side_effect=lambda **_: self.events.append("backup") or "a" * 64), \
             patch.object(cli, "locks", self.locked), \
             patch.object(cli, "make_runtime", return_value=(self.runtime, self.publisher, self.backend_store)), \
             patch.object(cli, "approved_pairs", return_value=("previous", "candidate")), \
             patch.object(cli, "CoordinatedEngine", return_value=self.engine):
            result = cli.deploy()
        self.assertEqual(self.events, ["approval", "backup", "lock", "unlock"])
        self.engine.deploy.assert_called_once_with("previous", "candidate", "a" * 64)
        self.assertEqual(result["frontendSnapshot"], "b" * 64)

    def test_deploy_when_pending_intent_exists_refuses_before_backup(self):
        self.store.load.return_value = {"phase": "recovering"}
        with patch.object(cli, "installed_tool", return_value=({}, "digest")), \
             patch.object(cli, "require_protected_tool"), patch.object(cli, "CoordinatedStore", return_value=self.store), \
             patch.object(cli, "Gate", return_value=self.gate), patch.object(cli, "capture") as capture:
            with self.assertRaisesRegex(DeploymentError, "RECOVERY_REQUIRED"):
                cli.deploy()
        capture.assert_not_called()

    def test_recover_when_journal_exists_neither_publishes_nor_captures(self):
        with patch.object(cli, "installed_tool", return_value=({}, "digest")), patch.object(cli, "CoordinatedStore", return_value=self.store), \
             patch.object(cli, "locks", self.locked), patch.object(cli, "make_runtime", return_value=(self.runtime, self.publisher, self.backend_store)), \
             patch.object(cli, "CoordinatedEngine", return_value=self.engine), \
             patch.object(cli, "require_protected_tool") as approval, patch.object(cli, "capture") as capture:
            self.assertEqual(cli.recover(), {"state": "rolledBack"})
        self.engine.recover.assert_called_once()
        approval.assert_not_called(); capture.assert_not_called()


if __name__ == "__main__":
    unittest.main()
