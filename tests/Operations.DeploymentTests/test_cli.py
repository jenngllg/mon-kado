"""Local command authorization, adoption and explicit recovery without external calls."""

import contextlib
import fcntl
import io
import json
import os
from pathlib import Path
import runpy
import signal
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

import deploy_cli as cli
from deploy_policy import DeploymentError, dotenv, initial_state
from deploy_storage import Store, atomic_write
from release_catalog import identifiers
from test_engine import release
from test_functional_smoke import ACCOUNT


class CliTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in ("STATE", "SETTINGS", "ROOT"):
            patcher = patch.object(cli, name, self.root)
            patcher.start()
            self.addCleanup(patcher.stop)
        patcher = patch.object(cli, "configuration_hash", return_value="c" * 64)
        patcher.start()
        self.addCleanup(patcher.stop)
        self.release = release()
        self.store = Store(self.root)
        self.state = initial_state() | {"phase": "succeeded", "current": self.release, "candidate": self.release}
        self.runtime = Mock()
        self.runtime.inspect.return_value = self.release["revision"]
        self.runtime.history.return_value = identifiers(self.release["migrationCatalog"])
        self.runtime.smoke.return_value = {"technical": True, "functional": True}
        self.runtime.observe_baseline.return_value = {"infrastructure": {}, "frontend": None}

    def test_utc_timestamp_and_safe_summary(self):
        self.assertTrue(cli.now().endswith("+00:00"))
        self.assertIsNone(cli.summary(initial_state())["revision"])
        value = cli.summary(self.state)
        self.assertEqual(self.release["revision"], value["revision"])
        self.assertEqual(self.release["publicationId"], value["publicationId"])
        self.assertNotIn("apiImage", value)

    def test_locks_exclude_other_descriptors_and_reuse_verified_outer_lock(self):
        target = self.root / "lock"
        with cli.lock(target):
            with self.assertRaisesRegex(DeploymentError, "COORDINATION_BUSY"), cli.lock(target):
                self.fail("Lock was bypassed")
        with cli.lock(target, inherited=99999):
            self.assertTrue(target.exists())
        descriptor = os.open(target, os.O_RDWR)
        other = os.open(self.root / "other", os.O_CREAT | os.O_RDWR, 0o600)
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            with cli.lock(target, inherited=descriptor):
                with self.assertRaisesRegex(DeploymentError, "COORDINATION_BUSY"), cli.lock(target, inherited=other):
                    self.fail("Unrelated descriptor bypassed lock")
        finally:
            os.close(descriptor)
            os.close(other)

    def test_signal_requests_one_controlled_interruption(self):
        with patch.object(cli.signal, "signal") as setter, self.assertRaises(InterruptedError):
            cli.interrupt(signal.SIGTERM, None)
        setter.assert_called_once_with(signal.SIGTERM, signal.SIG_DFL)

    def test_approved_channel_rejects_v1_and_validates_retained_manifests(self):
        runner = Mock(return_value=json.dumps({"tag_name": "backend-production", "draft": False,
                                              "prerelease": False, "body": json.dumps(self.release)}).encode())
        self.assertEqual(self.release, cli.approved_release("c" * 64, runner))
        self.assertIn("https://api.github.com/repos/jenngllg/mon-kado/releases/tags/backend-production", runner.call_args.args[0])
        cli.validate_saved(initial_state(), "c" * 64)
        cli.validate_saved(self.state, "c" * 64)
        legacy = {key: value for key, value in self.release.items() if key in
                  {"schemaVersion", "revision", "configurationHash", "apiImage", "workerImage"}} | {"schemaVersion": 1}
        runner.return_value = json.dumps({"tag_name": "backend-production", "draft": False,
                                          "prerelease": False, "body": json.dumps(legacy)}).encode()
        with self.assertRaisesRegex(DeploymentError, "PUBLICATION_V2_REQUIRED"):
            cli.approved_release("c" * 64, runner)
        with self.assertRaisesRegex(DeploymentError, "ADOPTION_REQUIRED"):
            cli.validate_saved(initial_state() | {"current": legacy}, "c" * 64)

    def test_adoption_verifies_exact_images_history_and_smoke_before_committing(self):
        atomic_write(self.root / "current.env", dotenv(self.release).encode())
        catalog = self.root / "catalog.json"
        atomic_write(catalog, json.dumps(self.release["migrationCatalog"]).encode())
        state = cli.adopt(catalog, self.store, self.runtime, "c" * 64)
        self.assertEqual("1-1", state["current"]["publicationId"])
        self.assertEqual("succeeded", state["phase"])
        self.runtime.verify_active.assert_called_once()
        self.runtime.smoke.assert_called_once()
        self.assertEqual(state, self.store.load())
        with self.assertRaisesRegex(DeploymentError, "ALREADY_ADOPTED"):
            cli.adopt(catalog, self.store, self.runtime, "c" * 64)

    def test_recovery_actions_require_exact_publication_and_verified_outcome(self):
        self.state.update(phase="recoveryRequired", previous=self.release, candidate=release("b"))
        self.store.save(self.state)
        for action in ("rollback", "accept"):
            result = cli.recover(action, "456-1", self.store, self.runtime)
            self.assertEqual("rolledBack" if action == "rollback" else "succeeded", result["phase"])
            self.store.save(self.state)
        with self.assertRaisesRegex(DeploymentError, "RECOVERY_ID_MISMATCH"):
            cli.recover("rollback", "incorrect", self.store, self.runtime)
        with self.assertRaisesRegex(DeploymentError, "INVALID_RECOVERY_ACTION"):
            cli.recover("unknown", "456-1", self.store, self.runtime)
        self.state.update(phase="rolledBack", rejectedPublication="456-1")
        self.store.save(self.state)
        value = cli.recover("acknowledge", "456-1", self.store, self.runtime)
        self.assertEqual("acknowledged", value["phase"])
        self.assertEqual("456-1", value["rejectedPublication"])

    def test_recovery_without_candidate_refuses_before_external_effects(self):
        self.store.save(initial_state())
        with self.assertRaisesRegex(DeploymentError, "RECOVERY_ID_MISMATCH"):
            cli.recover("accept", "456-1", self.store, self.runtime)
        self.assertEqual([], self.runtime.mock_calls)

    def test_cli_status_check_config_and_validation(self):
        self.store.save(self.state)
        self.assertEqual("succeeded", cli.execute(["status"])["phase"])
        atomic_write(self.root / "production.env", b"private")
        atomic_write(self.root / "deployment-smoke.json", json.dumps(ACCOUNT).encode())
        self.assertEqual({"configurationValid": True}, cli.execute(["check-config"]))
        with self.assertRaisesRegex(DeploymentError, "INVALID_ARGUMENTS"):
            cli.execute([])
        with patch.object(cli.os, "geteuid", return_value=1000), self.assertRaisesRegex(DeploymentError, "ROOT_REQUIRED"):
            cli.execute(["status"])

    def test_dispatch_all_operational_commands_under_coordination(self):
        self.store.save(self.state)
        with patch.object(cli, "Runtime", return_value=self.runtime), patch.object(cli, "approved_release", return_value=self.release), \
             patch.object(cli, "Engine") as engine, patch.object(cli.signal, "signal"), \
             patch.object(cli, "adopt", return_value=self.state) as adopt, patch.object(cli, "recover", return_value=self.state) as recover:
            engine.return_value.deploy.return_value = self.state
            self.assertEqual("succeeded", cli.execute(["deploy"])["phase"])
            engine.return_value.deploy.assert_called_once_with(self.release)
            self.state["rejectedPublication"] = self.release["publicationId"]
            self.store.save(self.state)
            self.assertEqual("succeeded", cli.execute(["deploy"])["phase"])
            engine.return_value.deploy.assert_called_once()
            self.assertEqual({"technical": True}, cli.execute(["smoke-readonly"]))
            self.assertTrue(cli.execute(["smoke-write"])["functional"])
            self.assertEqual("succeeded", cli.execute(["adopt", str(self.root / "catalog")])["phase"])
            adopt.assert_called_once()
            self.assertEqual("succeeded", cli.execute(["recover", "accept", "123-1"])["phase"])
            recover.assert_called_once()
        with patch.object(cli, "Runtime", return_value=self.runtime), patch.object(cli, "provision", return_value={"credentialsInstalled": True}) as provision:
            self.assertTrue(cli.execute(["provision-smoke"])["credentialsInstalled"])
            provision.assert_called_once_with(self.root, self.root, self.runtime.clients)

    def test_interrupted_rejected_publication_still_requires_recovery(self):
        self.state.update(phase="recoveryRequired", rejectedPublication=self.release["publicationId"])
        self.store.save(self.state)
        with patch.object(cli, "Runtime"), patch.object(cli, "approved_release", return_value=self.release), patch.object(cli.signal, "signal"):
            with self.assertRaisesRegex(DeploymentError, "RECOVERY_REQUIRED"):
                cli.execute(["deploy"])
        self.state["phase"] = "succeeded"
        self.store.save(self.state)
        with self.assertRaisesRegex(DeploymentError, "RECOVERY_NOT_REQUIRED"):
            cli.recover("rollback", self.release["publicationId"], self.store, self.runtime)

    def test_reviewed_reinstall_revalidates_active_baseline_without_erasing_rejection(self):
        self.state["rejectedPublication"] = "999-1"
        self.store.save(self.state)
        with patch.object(cli, "Runtime", return_value=self.runtime):
            self.assertEqual("succeeded", cli.execute(["rebaseline"])["phase"])
        result = self.store.load()
        self.assertEqual("999-1", result["rejectedPublication"])
        self.assertIsNone(result["candidate"])
        self.runtime.verify_active.assert_called_once_with(self.release)
        self.runtime.smoke.assert_called_once_with(self.release)
        self.state["phase"] = "recoveryRequired"
        self.store.save(self.state)
        with self.assertRaisesRegex(DeploymentError, "RECOVERY_REQUIRED"):
            cli.rebaseline(self.store, self.runtime, "c" * 64)

    def test_main_hides_errors_and_entrypoint_reports_readonly_status(self):
        for failure in (DeploymentError("TEST_FAILED"), ValueError("sensitive")):
            output = io.StringIO()
            with patch.object(cli, "execute", side_effect=failure), contextlib.redirect_stdout(output):
                self.assertEqual(1, cli.main(["deploy"]))
            self.assertNotIn("sensitive", output.getvalue())
        with patch.object(cli, "execute", return_value={"safe": True}), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, cli.main(["status"]))
        script = Path(cli.__file__)
        with patch.object(sys, "argv", [str(script), "status"]), patch("deploy_storage.Store.load", return_value=initial_state()), \
             contextlib.redirect_stdout(io.StringIO()), self.assertRaises(SystemExit) as result:
            runpy.run_path(str(script), run_name="__main__")
        self.assertEqual(0, result.exception.code)


if __name__ == "__main__":
    unittest.main()
