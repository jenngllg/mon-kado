"""Failure and restart scenarios for the coordinator's own installation."""

import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_install_engine import InstallationEngine
from deploy_policy import DeploymentError


class Store:
    def __init__(self):
        self.value = None
        self.receipt = None

    def load(self):
        return copy.deepcopy(self.value)

    def save(self, value):
        self.value = copy.deepcopy(value)

    def complete(self, value):
        self.receipt = copy.deepcopy(value)

    def clear(self):
        self.value = None


class Runtime:
    def __init__(self):
        self.events = []
        self.closed = False
        self.interrupt_after = None
        self.fail_at = None

    def effect(self, name):
        self.events.append(name)
        if self.fail_at == name:
            raise ValueError(name)
        if self.interrupt_after == name:
            self.interrupt_after = None
            raise SystemExit(name)

    def preflight(self, manifest, backup):
        if manifest != {"revision": "reviewed"} or backup != "first":
            raise ValueError("missing proof")
        self.effect("preflight")
        return {"backupFiles": {"old": "digest"}, "pair": {"old": "pair"}, "backupStarted": "utc-start"}

    def validate_intent(self, intent):
        if (intent.get("version") != 1 or intent.get("manifest") != {"revision": "reviewed"} or
                intent.get("backup") != "first" or intent.get("previousBackupFiles") != {"old": "digest"} or
                intent.get("previousPair") != {"old": "pair"} or
                intent.get("backupStarted") != "utc-start" or
                intent.get("phase") not in {"prepared", "staging", "guarding", "swappingBackup", "capturing", "verifying", "committed"} or
                (intent.get("phase") in {"verifying", "committed"} and intent.get("postInstallBackup") != "second")):
            raise ValueError("invalid intent")

    def revalidate_recovery(self, intent): self.effect("revalidate")
    def stage(self, intent): self.effect("stage")
    def install_guard(self, intent): self.effect("guard")
    def install_backup_code(self, intent): self.effect("swap")
    def verify_installed(self, intent): self.effect("verifyTool")
    def capture_installed_backup(self, intent):
        self.effect("backup")
        return "second"
    def verify_active_pair(self, intent): self.effect("verifyPair")
    def close_gate(self):
        self.closed = True
        self.effect("close")
    def open_gate(self):
        self.closed = False
        self.effect("open")


class InstallationEngineTests(unittest.TestCase):
    def test_install_when_checks_pass_records_two_distinct_backups(self):
        store, runtime = Store(), Runtime()
        result = InstallationEngine(store, runtime).install({"revision": "reviewed"}, "first")
        self.assertEqual(result, {"preInstallSnapshot": "first", "postInstallSnapshot": "second"})
        self.assertEqual(store.receipt["phase"], "committed")
        self.assertIsNone(store.load())
        self.assertFalse(runtime.closed)

    def test_install_when_preflight_fails_has_no_side_effects(self):
        store, runtime = Store(), Runtime()
        with self.assertRaises(ValueError):
            InstallationEngine(store, runtime).install({"revision": "unreviewed"}, "first")
        self.assertIsNone(store.load())
        self.assertEqual(runtime.events, [])

    def test_install_when_backup_or_verification_fails_retains_intent_and_closes_gate(self):
        for point in ("stage", "guard", "swap", "verifyTool", "backup", "verifyPair", "open"):
            with self.subTest(point=point):
                store, runtime = Store(), Runtime(); runtime.fail_at = point
                with self.assertRaisesRegex(DeploymentError, "TOOL_INSTALL_RECOVERY_REQUIRED"):
                    InstallationEngine(store, runtime).install({"revision": "reviewed"}, "first")
                self.assertIsNotNone(store.load())
                self.assertTrue(runtime.closed)
                self.assertIsNone(store.receipt)

    def test_recover_when_interrupted_at_any_effect_repeats_only_idempotent_steps(self):
        for point in ("stage", "guard", "close", "swap", "verifyTool", "backup", "verifyPair", "open"):
            with self.subTest(point=point):
                store, runtime = Store(), Runtime(); runtime.interrupt_after = point
                with self.assertRaises(SystemExit):
                    InstallationEngine(store, runtime).install({"revision": "reviewed"}, "first")
                result = InstallationEngine(store, runtime).recover()
                self.assertEqual(result["postInstallSnapshot"], "second")
                self.assertIsNone(store.load())
                self.assertFalse(runtime.closed)

    def test_recover_when_receipt_was_saved_reuses_original_snapshot(self):
        class InterruptedStore(Store):
            def complete(self, value):
                super().complete(value)
                raise SystemExit("receipt saved")

        store, runtime = InterruptedStore(), Runtime()
        with self.assertRaises(SystemExit):
            InstallationEngine(store, runtime).install({"revision": "reviewed"}, "first")
        saved = copy.deepcopy(store.receipt)
        store.complete = lambda value: self.assertEqual(value, saved)
        runtime.events.clear()
        InstallationEngine(store, runtime).recover()
        self.assertNotIn("backup", runtime.events)
        self.assertIsNone(store.load())


if __name__ == "__main__":
    unittest.main()
