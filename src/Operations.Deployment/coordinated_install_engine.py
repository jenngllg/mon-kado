"""Journal the first installation of independently approved coordination tools."""

import copy

from deploy_policy import DeploymentError, ensure


class InstallationEngine:
    """Finish an interrupted installation without exposing a half-installed tool.

    Effects must be idempotent and hold the deployment and backup locks. The
    existing application is not switched by this installation.
    """

    def __init__(self, store, runtime):
        self.store = store
        self.runtime = runtime

    def install(self, manifest, backup):
        """Record a fresh prior snapshot and exact old backup-code hashes."""
        ensure(self.store.load() is None, "TOOL_INSTALL_RECOVERY_REQUIRED")
        manifest = copy.deepcopy(manifest)
        previous = self.runtime.preflight(manifest, backup)
        intent = {"version": 1, "phase": "prepared", "manifest": manifest, "backup": backup,
                  "previousBackupFiles": previous["backupFiles"], "previousPair": previous["pair"],
                  "backupStarted": previous["backupStarted"], "postInstallBackup": None}
        self.runtime.validate_intent(intent)
        self.store.save(intent)
        return self._run(intent)

    def recover(self):
        """Resume the same pinned installation; do not invent a different candidate."""
        intent = self.store.load()
        ensure(intent is not None, "TOOL_INSTALL_RECOVERY_NOT_REQUIRED")
        self.runtime.validate_intent(intent)
        self.runtime.close_gate()
        self.runtime.revalidate_recovery(intent)
        if intent["phase"] == "committed":
            return self._finish(intent)
        return self._run(intent)

    def _finish(self, intent):
        try:
            self.runtime.verify_installed(intent)
            self.runtime.verify_active_pair(intent)
            self.runtime.open_gate()
            self.store.complete(intent)
            self.store.clear()
            return {"preInstallSnapshot": intent["backup"], "postInstallSnapshot": intent["postInstallBackup"]}
        except Exception as error:
            try:
                self.runtime.close_gate()
            except Exception:
                pass
            raise DeploymentError("TOOL_INSTALL_RECOVERY_REQUIRED") from error

    def _phase(self, intent, phase):
        intent["phase"] = phase
        self.runtime.validate_intent(intent)
        self.store.save(intent)

    def _run(self, intent):
        try:
            self._phase(intent, "staging")
            self.runtime.stage(intent)
            self._phase(intent, "guarding")
            self.runtime.install_guard(intent)
            self.runtime.close_gate()
            self._phase(intent, "swappingBackup")
            self.runtime.install_backup_code(intent)
            self.runtime.verify_installed(intent)
            self._phase(intent, "capturing")
            intent["postInstallBackup"] = self.runtime.capture_installed_backup(intent)
            self._phase(intent, "verifying")
            self.runtime.verify_active_pair(intent)
            self._phase(intent, "committed")
            self.runtime.open_gate()
            self.store.complete(intent)
            self.store.clear()
            return {"preInstallSnapshot": intent["backup"], "postInstallSnapshot": intent["postInstallBackup"]}
        except Exception as error:
            # An uncertain tool installation must not expose traffic after a
            # failed attempt; retain its journal for explicit resumption.
            try:
                self.runtime.close_gate()
            except Exception:
                pass
            raise DeploymentError("TOOL_INSTALL_RECOVERY_REQUIRED") from error
