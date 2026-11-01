"""Verify backup ordering and pinned recovery independently of native service effects."""

from contextlib import nullcontext
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

import coordinated_cli as cli
from deploy_policy import DeploymentError
from test_coordinated_policy import pair, candidate_pair, NOW, CONFIG


class OnlineCoordinatorTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.previous, self.candidate = pair("a", "e", "123-1"), candidate_pair()
        self.events = []
        self.state = {"phase": "succeeded", "current": copy.deepcopy(self.previous["backend"]), "baseline": {}}
        self.store = Mock(); self.store.load.side_effect = lambda: self.state
        self.runtime = Mock(); self.runtime.smoke.return_value = {"technical": True, "functional": True, "frontend": True}
        self.frontend = Mock()
        self.frontend.prepare_forward.side_effect = lambda *_: self.events.append("prepareFrontend")
        self.frontend.activate_forward.side_effect = lambda *_: self.events.append("activateFrontend")
        self.engine = Mock()
        def deploy(candidate):
            self.events.append("deployBackend")
            self.state.update(current=candidate, phase="succeeded")
            return self.state
        self.engine.deploy.side_effect = deploy
        self.backup = {}
        self.fail_frontend_backup = False
        def capture(**kwargs):
            self.events.append("backup")
            if kwargs.get("inherited") and self.fail_frontend_backup:
                raise DeploymentError("COORDINATED_BACKUP_FAILED")
            snapshot = ("2" if kwargs.get("inherited") else "1") * 64
            self.backup = {"snapshotId": snapshot, "error": None,
                           **{key: NOW.isoformat() for key in ("lastCapture", "lastRemoteCapture", "lastRemoteSuccess", "lastIntegrityCheck")}}
            return snapshot
        self.capture = Mock(side_effect=capture)
        replacements = {"STATE": self.root, "now": lambda: NOW, "configuration_hash": lambda _: CONFIG,
                        "Store": Mock(return_value=self.store), "OnlineRuntime": Mock(return_value=self.runtime),
                        "OnlineGate": Mock(), "CoordinatedFrontend": Mock(return_value=self.frontend),
                        "Engine": Mock(return_value=self.engine), "capture": self.capture,
                        "backup_command": lambda _: json.dumps(self.backup), "locks": lambda _: nullcontext(), "installed_tool": Mock()}
        for name, value in replacements.items():
            patcher = patch.object(cli, name, value); patcher.start(); self.addCleanup(patcher.stop)

    def test_each_component_switch_has_its_own_verified_capture_and_durable_receipt(self):
        result = cli.online_deploy(self.previous, self.candidate)
        self.assertEqual(["backup", "prepareFrontend", "deployBackend", "backup", "activateFrontend"], self.events)
        self.assertEqual("1" * 64, result["backendSnapshot"])
        self.assertEqual("2" * 64, result["frontendSnapshot"])
        self.assertFalse((self.root / "online-transition.json").exists())
        self.assertEqual(1, len(list(self.root.glob("online-receipt-*.json"))))

    def test_failed_frontend_capture_preserves_candidate_for_recovery_without_redeploying_backend(self):
        self.fail_frontend_backup = True
        with self.assertRaises(DeploymentError):
            cli.online_deploy(self.previous, self.candidate)
        self.frontend.activate_forward.assert_not_called()
        intent = json.loads((self.root / "online-transition.json").read_text())
        self.assertEqual("backendInstalled", intent["phase"])
        self.assertEqual(self.candidate, intent["candidate"])
        self.fail_frontend_backup = False
        result = cli.online_recover()
        self.assertEqual("installed", result["state"])
        self.engine.deploy.assert_called_once()

    def test_failed_initial_capture_never_prepares_or_switches_a_serving_component(self):
        self.capture.side_effect = DeploymentError("COORDINATED_BACKUP_FAILED")
        with self.assertRaises(DeploymentError):
            cli.online_deploy(self.previous, self.candidate)
        self.engine.deploy.assert_not_called()
        self.frontend.prepare_forward.assert_not_called()
        self.assertFalse((self.root / "online-transition.json").exists())

    def test_frontend_only_release_uses_one_capture_and_keeps_backend_running(self):
        candidate = copy.deepcopy(self.candidate)
        candidate["backend"] = copy.deepcopy(self.previous["backend"])
        candidate["frontend"]["backendRevision"] = candidate["backend"]["revision"]
        candidate["proof"]["backendRevision"] = candidate["backend"]["revision"]
        result = cli.online_deploy(self.previous, candidate)
        self.engine.deploy.assert_not_called()
        self.capture.assert_called_once()
        self.assertIsNone(result["backendSnapshot"])
        self.assertEqual("1" * 64, result["frontendSnapshot"])


if __name__ == "__main__":
    unittest.main()
