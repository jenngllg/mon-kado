"""Exercise coordinated publication through its public transition boundary."""

import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_engine import CoordinatedEngine, TransitionError


OLD = {"frontend": "old-frontend", "backend": "old-backend"}
NEW = {"frontend": "new-frontend", "backend": "new-backend"}
BACKUP = "verified-snapshot"
FRONTEND_BACKUP = "verified-second-snapshot"


class MemoryStore:
    """A durable test store which copies every committed intent."""

    def __init__(self):
        self.value = None
        self.interrupt_phase = None
        self.receipt = None
        self.fail_complete = False

    def load(self):
        return copy.deepcopy(self.value)

    def save(self, value):
        self.value = copy.deepcopy(value)
        if self.interrupt_phase is not None and self.interrupt_phase == value.get("phase"):
            self.interrupt_phase = None
            raise SystemExit(value["phase"])

    def clear(self):
        self.value = None

    def complete(self, value):
        if self.fail_complete:
            raise OSError("receipt unavailable")
        self.receipt = copy.deepcopy(value)


class Runtime:
    """Observable pair and maintenance gate with deterministic failure points."""

    def __init__(self):
        self.active = OLD.copy()
        self.gate_open = True
        self.calls = []
        self.interrupt_after = None
        self.fail_at = None

    def effect(self, name):
        self.calls.append(name)
        if self.fail_at == name:
            raise ValueError(name)
        if self.interrupt_after == name:
            self.interrupt_after = None
            raise SystemExit(name)

    def preflight(self, previous, candidate, backup):
        if previous != OLD or candidate != NEW or backup != BACKUP or self.active != OLD:
            raise ValueError("invalid evidence")
        self.effect("preflight")

    def validate_intent(self, intent):
        if (intent.get("version") != 1 or intent.get("previous") != OLD or
                intent.get("candidate") != NEW or intent.get("backup") != BACKUP or
                intent.get("toolDigest") != "tool-proof" or
                intent.get("frontendBackup") not in (None, FRONTEND_BACKUP) or
                (intent.get("phase") in {"installingFrontend", "verifyingCandidate", "committed"} and
                 intent.get("frontendBackup") != FRONTEND_BACKUP) or
                intent.get("phase") not in {"prepared", "closingGate", "installingBackend",
                                            "capturingFrontendBackup", "installingFrontend", "verifyingCandidate",
                                            "committed", "recovering"}):
            raise ValueError("invalid intent")

    def tool_digest(self):
        return "tool-proof"

    def close_gate(self):
        self.gate_open = False
        self.effect("closeGate")

    def install_backend(self, pair):
        if self.gate_open:
            raise AssertionError("backend switched while public")
        self.active["backend"] = pair["backend"]
        self.effect("installBackend")

    def install_frontend(self, pair):
        if self.gate_open:
            raise AssertionError("frontend switched while public")
        self.active["frontend"] = pair["frontend"]
        self.effect("installFrontend")

    def capture_frontend_backup(self, pair):
        if self.gate_open or self.active["backend"] != pair["backend"]:
            raise AssertionError("frontend backup captured in an unexpected state")
        self.effect("frontendBackup")
        return FRONTEND_BACKUP

    def restore_backend(self, pair):
        if self.gate_open:
            raise AssertionError("backend restored while public")
        self.active["backend"] = pair["backend"]
        self.effect("restoreBackend")

    def restore_frontend(self, pair):
        if self.gate_open:
            raise AssertionError("frontend restored while public")
        self.active["frontend"] = pair["frontend"]
        self.effect("restoreFrontend")

    def verify_pair(self, pair):
        if self.gate_open or self.active != pair:
            raise ValueError("pair verification failed")
        self.effect("verifyNew" if pair == NEW else "verifyOld")

    def open_gate(self):
        if self.active not in (OLD, NEW):
            raise AssertionError("mixed pair exposed")
        self.gate_open = True
        self.effect("openGate")


class CoordinatedEngineTests(unittest.TestCase):
    def test_deploy_when_preflight_fails_preserves_live_pair(self):
        store, runtime = MemoryStore(), Runtime()
        with self.assertRaises(ValueError):
            CoordinatedEngine(store, runtime).deploy(OLD, NEW, "unverified")
        self.assertIsNone(store.load())
        self.assertIsNone(store.receipt)
        self.assertEqual(runtime.active, OLD)
        self.assertTrue(runtime.gate_open)
        self.assertEqual(runtime.calls, [])

    def test_deploy_when_checks_pass_exposes_only_candidate_pair(self):
        store, runtime = MemoryStore(), Runtime()
        receipt = CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
        self.assertEqual(receipt, {"backendSnapshot": BACKUP, "frontendSnapshot": FRONTEND_BACKUP})
        self.assertEqual(store.receipt["frontendBackup"], FRONTEND_BACKUP)
        self.assertEqual(runtime.active, NEW)
        self.assertTrue(runtime.gate_open)
        self.assertIsNone(store.load())
        self.assertEqual(runtime.calls, ["preflight", "closeGate", "installBackend", "frontendBackup",
                                         "installFrontend", "verifyNew", "openGate"])

    def test_recover_when_interrupted_restores_pair_or_finishes_committed_pair(self):
        for step in ("closeGate", "installBackend", "frontendBackup", "installFrontend", "verifyNew", "openGate"):
            with self.subTest(step=step):
                store, runtime = MemoryStore(), Runtime()
                runtime.interrupt_after = step
                with self.assertRaises(SystemExit):
                    CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
                result = CoordinatedEngine(store, runtime).recover()
                expected = NEW if step == "openGate" else OLD
                self.assertEqual(result, "completed" if expected == NEW else "rolledBack")
                self.assertEqual(runtime.active, expected)
                self.assertTrue(runtime.gate_open)
                self.assertIsNone(store.load())

    def test_deploy_when_verification_fails_restores_previous_pair(self):
        store, runtime = MemoryStore(), Runtime()
        runtime.fail_at = "verifyNew"
        with self.assertRaisesRegex(TransitionError, "TRANSITION_ROLLED_BACK"):
            CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
        self.assertEqual(runtime.active, OLD)
        self.assertTrue(runtime.gate_open)
        self.assertIsNone(store.load())

    def test_deploy_when_second_backup_fails_never_installs_frontend(self):
        store, runtime = MemoryStore(), Runtime()
        runtime.fail_at = "frontendBackup"
        with self.assertRaisesRegex(TransitionError, "TRANSITION_ROLLED_BACK"):
            CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
        self.assertNotIn("installFrontend", runtime.calls)
        self.assertEqual(runtime.active, OLD)
        self.assertTrue(runtime.gate_open)

    def test_deploy_when_receipt_fails_keeps_candidate_closed_for_explicit_recovery(self):
        store, runtime = MemoryStore(), Runtime()
        store.fail_complete = True
        with self.assertRaisesRegex(TransitionError, "RECOVERY_REQUIRED"):
            CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
        self.assertEqual(store.load()["phase"], "committed")
        self.assertEqual(runtime.active, NEW)
        self.assertFalse(runtime.gate_open)
        store.fail_complete = False
        self.assertEqual(CoordinatedEngine(store, runtime).recover(), "completed")
        self.assertEqual(runtime.active, NEW)
        self.assertTrue(runtime.gate_open)
        self.assertIsNone(store.load())

    def test_recover_when_restore_fails_keeps_gate_closed_and_intent(self):
        store, runtime = MemoryStore(), Runtime()
        runtime.interrupt_after = "installFrontend"
        with self.assertRaises(SystemExit):
            CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
        runtime.fail_at = "restoreBackend"
        with self.assertRaises(ValueError):
            CoordinatedEngine(store, runtime).recover()
        self.assertFalse(runtime.gate_open)
        self.assertEqual(store.load()["phase"], "recovering")
        self.assertNotIn("openGate", runtime.calls)

    def test_recover_when_intent_is_invalid_has_no_effect(self):
        store, runtime = MemoryStore(), Runtime()
        store.save({"version": 99})
        with self.assertRaises(ValueError):
            CoordinatedEngine(store, runtime).recover()
        self.assertEqual(runtime.calls, [])
        self.assertTrue(runtime.gate_open)

    def test_recover_when_intent_was_durable_but_effect_did_not_run_restores_safely(self):
        for phase in ("prepared", "closingGate", "installingBackend", "capturingFrontendBackup", "installingFrontend",
                      "verifyingCandidate", "committed"):
            with self.subTest(phase=phase):
                store, runtime = MemoryStore(), Runtime()
                store.interrupt_phase = phase
                with self.assertRaises(SystemExit):
                    CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
                result = CoordinatedEngine(store, runtime).recover()
                expected = NEW if phase == "committed" else OLD
                self.assertEqual(result, "completed" if expected == NEW else "rolledBack")
                self.assertEqual(runtime.active, expected)
                self.assertTrue(runtime.gate_open)
                self.assertIsNone(store.load())

    def test_recover_when_committed_candidate_fails_recheck_restores_previous_pair(self):
        store, runtime = MemoryStore(), Runtime()
        runtime.interrupt_after = "openGate"
        with self.assertRaises(SystemExit):
            CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
        runtime.fail_at = "verifyNew"
        self.assertEqual(CoordinatedEngine(store, runtime).recover(), "rolledBack")
        self.assertEqual(runtime.active, OLD)
        self.assertTrue(runtime.gate_open)
        self.assertIsNone(store.load())

    def test_recover_when_interrupted_during_restore_can_be_repeated(self):
        for step in ("restoreBackend", "restoreFrontend", "verifyOld"):
            with self.subTest(step=step):
                store, runtime = MemoryStore(), Runtime()
                runtime.interrupt_after = "installFrontend"
                with self.assertRaises(SystemExit):
                    CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
                runtime.interrupt_after = step
                with self.assertRaises(SystemExit):
                    CoordinatedEngine(store, runtime).recover()
                self.assertFalse(runtime.gate_open)
                self.assertEqual(CoordinatedEngine(store, runtime).recover(), "rolledBack")
                self.assertEqual(runtime.active, OLD)
                self.assertTrue(runtime.gate_open)
                self.assertIsNone(store.load())

    def test_deploy_when_prior_intent_exists_refuses_to_start(self):
        store, runtime = MemoryStore(), Runtime()
        store.value = {"phase": "recovering"}
        with self.assertRaisesRegex(TransitionError, "RECOVERY_REQUIRED"):
            CoordinatedEngine(store, runtime).deploy(OLD, NEW, BACKUP)
        self.assertEqual(runtime.calls, [])


if __name__ == "__main__":
    unittest.main()
