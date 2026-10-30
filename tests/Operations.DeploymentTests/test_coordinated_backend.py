"""Check paired backend recovery through the existing native deployment engine."""

import hashlib
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Deployment", "src/Operations.Frontend", "deployments/production",
             "tests/Operations.DeploymentTests"):
    sys.path.insert(0, str(ROOT / path))
import frontend_runtime as hosting
from coordinated_backend import CoordinatedBackend
from deploy_policy import DeploymentError, initial_state
from release_catalog import identifiers
from test_engine import release


class CoordinatedBackendTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        releases = Path(self.temporary.name) / "releases"; releases.mkdir()
        for name in ("e" * 40, "f" * 40):
            (releases / name).mkdir()
        (releases / "current").symlink_to("e" * 40, target_is_directory=True)
        self.previous = {"backend": release("a"), "frontend": {"revision": "e" * 40, "googleEnabled": True}}
        self.candidate = {"backend": release("b"), "frontend": {"revision": "f" * 40, "googleEnabled": True}}
        self.state = initial_state() | {"phase": "succeeded", "current": self.previous["backend"],
                                       "baseline": {"infrastructure": {}, "frontend": {"revision": "e" * 40}}}
        self.store = Mock(spec=["load", "save", "begin", "finish", "reconcile"])
        self.store.load.return_value = self.state
        self.runtime = Mock(spec=["verify_active", "preflight", "history", "stop", "start", "migrate", "smoke", "restore_baseline", "clients"])
        self.runtime.history.return_value = identifiers(self.previous["backend"]["migrationCatalog"])
        self.runtime.preflight.return_value = {"infrastructure": {}, "frontend": {"revision": "e" * 40}}
        self.runtime.smoke.return_value = {"technical": True, "functional": True, "frontend": True}
        self.frontend = Mock(); self.frontend.publisher.releases = releases
        self.gate = Mock(); self.approved = Mock(return_value=self.candidate["backend"])
        self.coordinator = CoordinatedBackend(self.store, self.runtime, self.frontend, self.gate,
                                              lambda: "2026-10-30T13:00:00+00:00", self.approved)

    def test_install_and_restore_when_candidate_frontend_is_active_restores_prior_binaries(self):
        self.coordinator.prepare(self.previous, self.candidate)
        self.coordinator.install(self.previous, self.candidate)
        self.assertEqual(self.state["current"], self.candidate["backend"])
        hosting.switch(self.frontend.publisher.releases, "f" * 40)
        self.coordinator.restore(self.previous, self.candidate)
        self.assertEqual(self.state["current"], self.previous["backend"])
        self.assertEqual(self.state["phase"], "rolledBack")
        self.assertEqual(self.state["rejectedPublication"], self.candidate["backend"]["publicationId"])
        self.runtime.migrate.assert_not_called()
        self.runtime.restore_baseline.assert_called_once()
        self.assertEqual(self.runtime.restore_baseline.call_args.args[0]["frontend"]["revision"], "f" * 40)
        self.frontend.verify_retained.assert_called_once_with(self.candidate)

    def test_install_when_approval_changes_refuses_before_stopping_services(self):
        self.approved.return_value = self.previous["backend"]
        with self.assertRaisesRegex(DeploymentError, "BACKEND_APPROVAL_CHANGED"):
            self.coordinator.install(self.previous, self.candidate)
        self.runtime.stop.assert_not_called()

    def test_restore_when_schema_history_changed_refuses_database_rollback(self):
        self.coordinator.install(self.previous, self.candidate)
        self.runtime.history.return_value = []
        self.runtime.stop.reset_mock()
        with self.assertRaises(DeploymentError):
            self.coordinator.restore(self.previous, self.candidate)
        self.runtime.stop.assert_not_called()
        self.runtime.migrate.assert_not_called()

    def test_restore_before_backend_change_does_not_switch_executables(self):
        self.coordinator.restore(self.previous, self.candidate)
        self.runtime.verify_active.assert_called_once_with(self.previous["backend"])
        self.runtime.stop.assert_not_called()
        self.runtime.start.assert_not_called()

    def test_verify_contract_when_actual_api_schema_differs_refuses(self):
        schema = b'{"openapi":"3.1.0"}'
        candidate = self.candidate | {"proof": {"openapiSha256": hashlib.sha256(schema).hexdigest()}}
        self.runtime.clients.return_value.request.return_value = (200, {}, schema)
        self.coordinator.verify_contract(candidate)
        self.runtime.clients.return_value.request.return_value = (200, {}, b"changed")
        with self.assertRaisesRegex(DeploymentError, "BACKEND_CONTRACT_CHANGED"):
            self.coordinator.verify_contract(candidate)


if __name__ == "__main__":
    unittest.main()
