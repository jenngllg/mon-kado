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
        def capture(**kwargs):
            self.events.append("backup")
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
        publisher = Mock()
        publisher.releases = self.root / "releases"
        patcher = patch.object(cli.hosting, "Deployment", return_value=publisher)
        patcher.start(); self.addCleanup(patcher.stop)

    def test_both_component_switches_share_one_verified_capture_and_durable_receipt(self):
        result = cli.online_deploy(self.previous, self.candidate)
        self.assertEqual(["backup", "prepareFrontend", "deployBackend", "activateFrontend"], self.events)
        self.assertEqual("1" * 64, result["backendSnapshot"])
        self.assertEqual("1" * 64, result["frontendSnapshot"])
        self.capture.assert_called_once()
        self.assertFalse((self.root / "online-transition.json").exists())
        self.assertEqual(1, len(list(self.root.glob("online-receipt-*.json"))))

    def test_failed_frontend_activation_resumes_the_pinned_pair_without_another_capture(self):
        self.frontend.activate_forward.side_effect = [DeploymentError("FRONTEND_ACTIVATION_FAILED"), None]
        with self.assertRaises(DeploymentError):
            cli.online_deploy(self.previous, self.candidate)
        self.assertEqual(1, self.frontend.activate_forward.call_count)
        intent = json.loads((self.root / "online-transition.json").read_text())
        self.assertEqual("backendInstalled", intent["phase"])
        self.assertEqual(self.candidate, intent["candidate"])
        self.assertEqual(2, intent["version"])
        self.assertEqual(intent["backendSnapshot"], intent["frontendSnapshot"])
        result = cli.online_recover()
        self.assertEqual("installed", result["state"])
        self.engine.deploy.assert_called_once()
        self.capture.assert_called_once()

    def test_legacy_interrupted_operation_retains_its_original_two_capture_recovery(self):
        self.frontend.activate_forward.side_effect = [DeploymentError("FRONTEND_ACTIVATION_FAILED"), None]
        with self.assertRaises(DeploymentError):
            cli.online_deploy(self.previous, self.candidate)
        path = self.root / "online-transition.json"
        intent = json.loads(path.read_text())
        intent.update(version=1, frontendSnapshot=None)
        path.write_text(json.dumps(intent))
        result = cli.online_recover()
        self.assertEqual("2" * 64, result["frontendSnapshot"])
        self.engine.deploy.assert_called_once()
        self.assertEqual(2, self.capture.call_count)

    def test_unverified_shared_backup_blocks_both_serving_switches(self):
        with patch.object(cli, "backup_command", return_value=json.dumps({"snapshotId": "1" * 64, "error": "failed"})):
            with self.assertRaises(DeploymentError):
                cli.online_deploy(self.previous, self.candidate)
        self.engine.deploy.assert_not_called()
        self.frontend.activate_forward.assert_not_called()

    def test_changed_backup_status_after_api_switch_blocks_frontend_without_recapture(self):
        original = self.engine.deploy.side_effect
        def deploy(candidate):
            result = original(candidate)
            self.backup["error"] = "integrity failed"
            return result
        self.engine.deploy.side_effect = deploy
        with self.assertRaises(DeploymentError):
            cli.online_deploy(self.previous, self.candidate)
        self.frontend.activate_forward.assert_not_called()
        self.capture.assert_called_once()
        self.backup["error"] = None
        result = cli.online_recover()
        self.assertEqual("installed", result["state"])
        self.engine.deploy.assert_called_once()
        self.capture.assert_called_once()

    def test_recovery_rejects_divergent_shared_backup_references(self):
        self.frontend.activate_forward.side_effect = DeploymentError("FRONTEND_ACTIVATION_FAILED")
        with self.assertRaises(DeploymentError):
            cli.online_deploy(self.previous, self.candidate)
        path = self.root / "online-transition.json"
        intent = json.loads(path.read_text())
        intent["frontendSnapshot"] = "2" * 64
        path.write_text(json.dumps(intent))
        self.frontend.activate_forward.reset_mock()
        with self.assertRaises(DeploymentError):
            cli.online_recover()
        self.frontend.activate_forward.assert_not_called()
        self.capture.assert_called_once()

    def test_backend_only_release_uses_one_capture_and_retains_frontend_approval(self):
        candidate = copy.deepcopy(self.candidate)
        candidate["frontend"] = copy.deepcopy(self.previous["frontend"])
        candidate["proof"] = copy.deepcopy(self.previous.get("proof"))
        result = cli.online_deploy(self.previous, candidate)
        self.engine.deploy.assert_called_once()
        self.frontend.prepare_forward.assert_not_called()
        self.capture.assert_called_once()
        self.assertEqual("1" * 64, result["backendSnapshot"])
        self.assertIsNone(result["frontendSnapshot"])

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

    def test_repeated_ci_request_for_installed_pair_does_not_take_another_backup(self):
        heads = {"mon-kado": self.candidate["backend"]["revision"], "mon-kado-front": self.candidate["frontend"]["revision"]}
        request = self.candidate["frontend"]["revision"]
        runtime = Mock()
        runtime.clients.return_value.request.return_value = (200, {}, b'{}')
        with patch.object(cli, "installed_tool", return_value=({}, "digest")), \
             patch.object(cli, "require_protected_tool"), patch.object(cli, "CoordinatedStore"), \
             patch.object(cli, "Gate") as gate, patch.object(cli, "Runtime", return_value=runtime), \
             patch.object(cli, "make_forward_runtime", return_value=(Mock(), Mock(), self.store)), \
             patch.object(cli, "approved_forward_pairs", side_effect=[(self.previous, self.candidate), (self.candidate, self.candidate)]), \
             patch.object(cli, "main_head", side_effect=lambda repository: heads[repository]), \
             patch.object(cli, "github_bytes", return_value=b'{}'), \
             patch.object(cli, "online_compatible"), patch.object(cli, "verify_candidate_schema"), \
             patch.object(cli, "online_deploy", return_value={"state": "installed"}) as deploy:
            gate.return_value.exists.return_value = False
            cli.CoordinatedStore.return_value.load.return_value = None
            self.assertEqual("installed", cli.main_deploy("frontend", request)["state"])
            self.assertEqual("alreadyInstalled", cli.main_deploy("frontend", request)["state"])
            deploy.assert_called_once_with(self.previous, self.candidate)
        self.capture.assert_not_called()

    def test_late_unpublished_frontend_merge_blocks_backend_before_backup(self):
        candidate = copy.deepcopy(self.candidate)
        candidate["frontend"] = self.previous["frontend"]
        heads = {"mon-kado": candidate["backend"]["revision"], "mon-kado-front": self.candidate["frontend"]["revision"]}
        with patch.object(cli, "installed_tool", return_value=({}, "digest")), \
             patch.object(cli, "require_protected_tool"), patch.object(cli, "CoordinatedStore"), \
             patch.object(cli, "Gate") as gate, \
             patch.object(cli, "make_forward_runtime", return_value=(Mock(), Mock(), self.store)), \
             patch.object(cli, "approved_forward_pairs", return_value=(self.previous, candidate)), \
             patch.object(cli, "main_head", side_effect=lambda repository: heads[repository]):
            gate.return_value.exists.return_value = False
            cli.CoordinatedStore.return_value.load.return_value = None
            with self.assertRaisesRegex(DeploymentError, "PAIRED_PUBLICATION_REQUIRED"):
                cli.main_deploy("backend", candidate["backend"]["revision"])
        self.capture.assert_not_called()


if __name__ == "__main__":
    unittest.main()
