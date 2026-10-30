"""Durable sequencing for a verified backend/frontend pair.

The runtime owns all effect-specific validation, including the backup proof,
immutable manifests, exclusive locks and maintenance gate. This module never
interprets a revision as proof of compatibility and does not run migrations.
"""

import copy


class TransitionError(Exception):
    """Report an incomplete transition without exposing provider data."""


class CoordinatedEngine:
    """Coordinate a pair under an already-held exclusive deployment lock.

    Runtime methods are idempotent: after a process interruption, an operation
    may have taken effect even when its return value was never observed. The
    store persists and fsyncs intent before each external effect.
    """

    def __init__(self, store, runtime):
        self.store = store
        self.runtime = runtime

    def deploy(self, previous, candidate, backup):
        """Install a previously approved pair or leave recoverable intent."""
        if self.store.load() is not None:
            raise TransitionError("RECOVERY_REQUIRED")
        previous, candidate = copy.deepcopy(previous), copy.deepcopy(candidate)
        # No persistent intent or maintenance until every preflight succeeds.
        self.runtime.preflight(previous, candidate, backup)
        intent = {"version": 1, "phase": "prepared", "previous": previous,
                  "candidate": candidate, "backup": backup, "frontendBackup": None,
                  "toolDigest": self.runtime.tool_digest()}
        self.runtime.validate_intent(intent)
        self.store.save(intent)
        try:
            self._install(intent)
        except Exception as error:
            try:
                self._restore(intent)
            except Exception:
                raise TransitionError("RECOVERY_REQUIRED") from None
            raise TransitionError("TRANSITION_ROLLED_BACK") from error

    def recover(self):
        """Reconcile interrupted effects before permitting any new transition."""
        intent = self.store.load()
        if intent is None:
            return "nothingToRecover"
        self.runtime.validate_intent(intent)
        if intent["phase"] == "committed":
            try:
                self.runtime.close_gate()
                self.runtime.verify_pair(intent["candidate"])
                self.runtime.open_gate()
                self.store.clear()
                return "completed"
            except Exception:
                # A committed candidate is not trusted after a failed recheck.
                pass
        self._restore(intent)
        return "rolledBack"

    def _save_phase(self, intent, phase):
        intent["phase"] = phase
        self.runtime.validate_intent(intent)
        self.store.save(intent)

    def _install(self, intent):
        self._save_phase(intent, "closingGate")
        self.runtime.close_gate()
        self._save_phase(intent, "installingBackend")
        self.runtime.install_backend(intent["candidate"])
        # The frontend is a separate production change and needs its own new
        # coherent, verified offsite snapshot after the backend rollout.
        self._save_phase(intent, "capturingFrontendBackup")
        intent["frontendBackup"] = self.runtime.capture_frontend_backup(intent["candidate"])
        self.runtime.validate_intent(intent)
        self._save_phase(intent, "installingFrontend")
        self.runtime.install_frontend(intent["candidate"])
        self._save_phase(intent, "verifyingCandidate")
        self.runtime.verify_pair(intent["candidate"])
        # A crash before this durable commit must restore the old pair.
        self._save_phase(intent, "committed")
        self.runtime.open_gate()
        self.store.clear()

    def _restore(self, intent):
        # Close first even if the recorded phase predates closing: an operation
        # may have succeeded before the process lost its acknowledgement.
        self._save_phase(intent, "recovering")
        self.runtime.close_gate()
        self.runtime.restore_backend(intent["previous"])
        self.runtime.restore_frontend(intent["previous"])
        self.runtime.verify_pair(intent["previous"])
        self.runtime.open_gate()
        self.store.clear()
