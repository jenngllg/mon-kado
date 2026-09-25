"""Failure-injection tests exercise the public orchestrator without production access."""

import copy
import unittest
from unittest.mock import Mock

from deploy_engine import Engine
from deploy_policy import DeploymentError, decision, dotenv, initial_state, validate_state
from release_catalog import fingerprint, identifiers


def release(revision="a", extra=False):
    catalog = {"schemaVersion": 1, "modelHash": "d" * 64,
               "migrations": [{"id": "20260101000000_Initial", "sha256": "b" * 64}]}
    if extra:
        catalog["migrations"].append({"id": "20260201000000_Next", "sha256": "c" * 64})
    return {"schemaVersion": 2, "publicationId": "123-1" if revision == "a" else "456-1",
            "revision": revision * 40, "configurationHash": "c" * 64,
            "apiImage": "ghcr.io/jenngllg/mon-kado-api@sha256:" + revision * 64,
            "workerImage": "ghcr.io/jenngllg/mon-kado-worker@sha256:" + revision * 64,
            "migrationCatalog": catalog, "migrationHash": fingerprint(catalog), "rollbackAllowed": True}


class PolicyTests(unittest.TestCase):
    def test_history_and_compatibility(self):
        current = release()
        history = identifiers(current["migrationCatalog"])
        self.assertEqual({"migrate": True, "rollback": False}, decision(None, current, []))
        self.assertEqual({"migrate": False, "rollback": True}, decision(current, release("b"), history))
        self.assertEqual({"migrate": True, "rollback": False}, decision(current, release("b", True), history))
        self.assertFalse(decision(current, {**release("b"), "rollbackAllowed": False}, history)["rollback"])
        for candidate, active, database, code in (
            (current, None, history, "ADOPTION_REQUIRED"),
            (current, current, [], "DATABASE_HISTORY_DIVERGED"),
            ({**release("b"), "configurationHash": "a" * 64}, current, history, "CONFIGURATION_BASELINE_REQUIRED"),
        ):
            with self.subTest(code=code), self.assertRaisesRegex(DeploymentError, code):
                decision(active, candidate, database)
        for rewrite in ("migration", "model"):
            candidate = release("b")
            if rewrite == "migration":
                candidate["migrationCatalog"]["migrations"][0]["sha256"] = "e" * 64
            else:
                candidate["migrationCatalog"]["modelHash"] = "e" * 64
            candidate["migrationHash"] = fingerprint(candidate["migrationCatalog"])
            with self.subTest(rewrite=rewrite), self.assertRaises(DeploymentError):
                decision(current, candidate, history)
        with self.assertRaisesRegex(ValueError, "ADOPTION_REQUIRED"):
            decision(None, {"schemaVersion": 1}, [])
        with self.assertRaises(ValueError):
            decision(None, {**current, "migrationHash": "e" * 64}, [])

    def test_private_compatibility_contract(self):
        value = release()
        self.assertEqual("API_IMAGE=" + value["apiImage"] + "\nWORKER_IMAGE=" + value["workerImage"] +
                         "\nRELEASE_REVISION=" + value["revision"] + "\n", dotenv(value))
        self.assertEqual(initial_state(), validate_state(initial_state()))
        for invalid in (None, {}, {**initial_state(), "schemaVersion": True},
                        {**initial_state(), "phase": []}, {**initial_state(), "phase": "invalid"},
                        {**initial_state(), "checks": []}):
            with self.subTest(value=invalid), self.assertRaisesRegex(ValueError, "INVALID_DEPLOYMENT_STATE"):
                validate_state(invalid)


class EngineTests(unittest.TestCase):
    def setUp(self):
        self.current = release()
        self.candidate = release("b")
        self.state = initial_state() | {"current": self.current, "phase": "succeeded"}
        self.store = Mock(spec=["load", "save", "begin", "finish", "reconcile"])
        self.store.load.return_value = self.state
        self.saved = []
        self.store.save.side_effect = lambda value: self.saved.append(copy.deepcopy(value))
        self.runtime = Mock(spec=["verify_active", "preflight", "history", "stop", "start", "migrate", "smoke"])
        self.runtime.history.return_value = identifiers(self.current["migrationCatalog"])
        self.runtime.preflight.return_value = {"infrastructure": {}, "frontend": None}
        self.runtime.smoke.return_value = {"technical": True, "functional": True}
        self.engine = Engine(self.store, self.runtime, lambda: "2026-09-25T14:00:00+00:00")

    def test_rollout_commits_only_after_all_checks_without_migrations(self):
        result = self.engine.deploy(self.candidate)
        self.assertEqual("succeeded", result["phase"])
        self.assertEqual(self.candidate, result["current"])
        self.assertEqual(self.current, result["previous"])
        self.runtime.preflight.assert_called_once_with(self.candidate, self.current)
        self.runtime.migrate.assert_not_called()
        self.runtime.start.assert_called_once_with(self.candidate)
        self.runtime.smoke.assert_called_once_with(self.candidate)
        self.store.begin.assert_called_once()
        self.store.finish.assert_called_once_with(result)
        self.assertEqual(["stopping", "starting", "verifying"], [value["phase"] for value in self.saved])

    def test_repeat_has_no_rollout_and_repairs_derived_state(self):
        self.assertEqual(self.state, self.engine.deploy(self.current))
        self.runtime.verify_active.assert_called_once_with(self.current)
        self.store.reconcile.assert_called_once_with(self.state)
        self.runtime.preflight.assert_not_called()
        self.runtime.stop.assert_not_called()

    def test_interrupted_and_rejected_attempts_never_touch_runtime(self):
        for field, value, code in (("phase", "migrating", "RECOVERY_REQUIRED"),
                                   ("rejectedPublication", self.candidate["publicationId"], "PUBLICATION_REJECTED")):
            self.store.load.return_value = initial_state() | {field: value}
            with self.subTest(code=code), self.assertRaisesRegex(DeploymentError, code):
                self.engine.deploy(self.candidate)
        self.assertEqual([], self.runtime.mock_calls)

    def test_preflight_failure_leaves_services_untouched_and_prevents_repeated_account_logins(self):
        self.runtime.preflight.side_effect = DeploymentError("INSUFFICIENT_DISK")
        with self.assertRaisesRegex(DeploymentError, "INSUFFICIENT_DISK"):
            self.engine.deploy(self.candidate)
        self.runtime.stop.assert_not_called()
        self.store.begin.assert_not_called()
        self.store.save.assert_called_once()
        self.assertEqual("rejected", self.state["phase"])
        with self.assertRaisesRegex(DeploymentError, "PUBLICATION_REJECTED"):
            self.engine.deploy(self.candidate)
        self.runtime.preflight.assert_called_once()

    def test_malformed_preflight_result_is_safely_rejected(self):
        for failure in (ValueError("private diagnostic"), AttributeError("private diagnostic")):
            self.state.update(phase="succeeded", rejectedPublication=None)
            self.runtime.preflight.side_effect = failure
            with self.subTest(failure=type(failure).__name__), self.assertRaisesRegex(DeploymentError, "^PREFLIGHT_FAILED$"):
                self.engine.deploy(self.candidate)
            self.assertEqual("rejected", self.state["phase"])
            self.runtime.stop.assert_not_called()

    def test_failed_smoke_restores_previous_and_preserves_rejected_publication(self):
        self.runtime.smoke.side_effect = [DeploymentError("SMOKE_FAILED"), {"technical": True}]
        with self.assertRaisesRegex(DeploymentError, "PUBLICATION_ROLLED_BACK"):
            self.engine.deploy(self.candidate)
        self.assertEqual("rolledBack", self.state["phase"])
        self.assertEqual(self.current, self.state["current"])
        self.assertEqual(self.candidate["publicationId"], self.state["rejectedPublication"])
        self.assertEqual("SMOKE_FAILED", self.state["error"])
        self.assertEqual(2, self.runtime.start.call_count)
        self.runtime.migrate.assert_not_called()

    def test_caught_interruption_can_recover_once(self):
        self.runtime.start.side_effect = [InterruptedError(), None]
        with self.assertRaisesRegex(DeploymentError, "PUBLICATION_ROLLED_BACK"):
            self.engine.deploy(self.candidate)
        self.assertEqual("INTERRUPTED", self.state["error"])
        self.assertEqual("rolledBack", self.state["phase"])

    def test_rollback_failure_preserves_recovery_marker(self):
        self.runtime.smoke.side_effect = DeploymentError("SMOKE_FAILED")
        with self.assertRaisesRegex(DeploymentError, "ROLLBACK_FAILED"):
            self.engine.deploy(self.candidate)
        self.assertEqual("recoveryRequired", self.state["phase"])
        self.assertEqual("ROLLBACK_FAILED", self.state["error"])
        self.store.finish.assert_not_called()

    def test_database_drift_prohibits_rollback(self):
        self.runtime.history.side_effect = [identifiers(self.current["migrationCatalog"]), [], []]
        with self.assertRaisesRegex(DeploymentError, "ROLLBACK_FAILED"):
            self.engine.deploy(self.candidate)
        self.assertEqual(1, self.runtime.start.call_count)
        self.store.finish.assert_not_called()

    def test_changed_schema_migration_failure_never_rolls_back(self):
        self.runtime.migrate.side_effect = DeploymentError("MIGRATION_FAILED")
        with self.assertRaisesRegex(DeploymentError, "RECOVERY_REQUIRED"):
            self.engine.deploy(release("b", True))
        self.assertEqual("recoveryRequired", self.state["phase"])
        self.runtime.start.assert_not_called()
        self.store.finish.assert_not_called()

    def test_successful_migration_and_first_rollout(self):
        for current in (self.current, None):
            candidate = release("b", True)
            self.store.load.return_value = initial_state() | {"phase": "succeeded" if current else "idle", "current": current}
            self.runtime.history.side_effect = [identifiers(current["migrationCatalog"]) if current else [],
                                                identifiers(candidate["migrationCatalog"])]
            with self.subTest(adopted=current is not None):
                result = self.engine.deploy(candidate)
                self.assertEqual("succeeded", result["phase"])
        self.assertEqual(2, self.runtime.migrate.call_count)


if __name__ == "__main__":
    unittest.main()
