"""Schema transitions reconcile forward with a durable closed traffic gate."""

import copy
import unittest

from test_coordinated_engine import MemoryStore, Runtime, OLD, NEW, BACKUP, FRONTEND_BACKUP
from coordinated_engine import ForwardEngine, TransitionError


class ForwardRuntime(Runtime):
    def validate_intent(self, intent):
        value = copy.deepcopy(intent)
        if value.get("version") != 2:
            raise ValueError("invalid version")
        value["version"] = 1
        super().validate_intent(value)


class ForwardTests(unittest.TestCase):
    def setUp(self):
        self.store = MemoryStore()
        self.runtime = ForwardRuntime()
        self.engine = ForwardEngine(self.store, self.runtime)

    def test_complete_pair_has_two_snapshots_and_never_rolls_back(self):
        self.assertEqual("installed", self.engine.deploy(OLD, NEW, BACKUP))
        self.assertEqual(NEW, self.runtime.active)
        self.assertTrue(self.runtime.gate_open)
        self.assertIsNone(self.store.load())
        self.assertEqual(FRONTEND_BACKUP, self.store.receipt["frontendBackup"])
        self.assertNotIn("restoreBackend", self.runtime.calls)
        self.assertNotIn("restoreFrontend", self.runtime.calls)

    def test_failed_effect_keeps_gate_and_intent_then_explicit_recovery_completes_candidate(self):
        for effect in ("installBackend", "frontendBackup", "installFrontend", "verifyNew", "openGate"):
            with self.subTest(effect=effect):
                self.setUp()
                self.runtime.fail_at = effect
                with self.assertRaisesRegex(TransitionError, "FORWARD_RECOVERY_REQUIRED"):
                    self.engine.deploy(OLD, NEW, BACKUP)
                self.assertFalse(self.runtime.gate_open)
                self.assertIsNotNone(self.store.load())
                self.assertNotIn("restoreBackend", self.runtime.calls)
                self.runtime.fail_at = None
                self.assertEqual("installed", self.engine.recover())
                self.assertEqual(NEW, self.runtime.active)
                self.assertTrue(self.runtime.gate_open)

    def test_process_interruption_at_each_durable_phase_recovers_exact_candidate(self):
        for phase in ("prepared", "installingBackend", "capturingFrontendBackup", "installingFrontend", "verifyingCandidate", "committed"):
            with self.subTest(phase=phase):
                self.setUp()
                self.store.interrupt_phase = phase
                with self.assertRaises(SystemExit):
                    self.engine.deploy(OLD, NEW, BACKUP)
                self.assertIsNotNone(self.store.load())
                self.assertEqual("installed", self.engine.recover())
                self.assertEqual(NEW, self.runtime.active)

    def test_lost_effect_acknowledgements_and_receipt_failures_are_reconciled(self):
        for effect in ("closeGate", "installBackend", "frontendBackup", "installFrontend", "verifyNew", "openGate"):
            with self.subTest(effect=effect):
                self.setUp()
                self.runtime.interrupt_after = effect
                with self.assertRaises(SystemExit):
                    self.engine.deploy(OLD, NEW, BACKUP)
                self.assertEqual("installed", self.engine.recover())
        self.setUp()
        self.store.fail_complete = True
        with self.assertRaises(TransitionError):
            self.engine.deploy(OLD, NEW, BACKUP)
        self.assertFalse(self.runtime.gate_open)
        self.store.fail_complete = False
        self.engine.recover()
        self.assertTrue(self.runtime.gate_open)

    def test_preflight_and_existing_or_tampered_intents_never_start_a_new_transition(self):
        self.runtime.fail_at = "preflight"
        with self.assertRaises(ValueError):
            self.engine.deploy(OLD, NEW, BACKUP)
        self.assertIsNone(self.store.load())
        self.assertTrue(self.runtime.gate_open)
        self.store.value = {"version": 99}
        with self.assertRaises(TransitionError):
            self.engine.deploy(OLD, NEW, BACKUP)
        with self.assertRaises(ValueError):
            self.engine.recover()
        self.store.value = None
        self.assertEqual("nothingToRecover", self.engine.recover())


if __name__ == "__main__":
    unittest.main()
