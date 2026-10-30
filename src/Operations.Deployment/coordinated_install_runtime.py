"""Native effects for first installation of the reviewed coordination tools."""

import copy
from datetime import datetime, timezone
from pathlib import Path

import frontend_runtime as hosting
import frontend_contract
from coordinated_backup import capture, run as backup_command
from coordinated_install_files import activate_backup, backup_hashes, stage_guard, stage_tool
from coordinated_install_policy import validate_install_intent
from coordinated_integrity import BACKUP_FILES, FILES, content_digest, validate, verify_installed
from coordinated_policy import validate_backup
from deploy_policy import DeploymentError, ensure
from monkado_backup.operations import lock as backup_lock


class InstallationRuntime:
    """Keep historical files and the current application pair intact throughout install."""

    def __init__(self, source, root, state, settings, backup_parent, systemd, backend_store,
                 native, publisher, gate, configuration, approval, backup_state, backup_started,
                 clock=lambda: datetime.now(timezone.utc), backup_capture=capture,
                 backup_state_directory=Path("/var/lib/monkado-backup")):
        self.source, self.root, self.state, self.settings, self.backup_parent, self.systemd = map(
            Path, (source, root, state, settings, backup_parent, systemd))
        self.backend_store, self.native, self.publisher, self.gate = backend_store, native, publisher, gate
        self.configuration, self.approval, self.backup_state = configuration, approval, backup_state
        self.backup_started, self.clock, self.backup_capture = backup_started, clock, backup_capture
        self.backup_state_directory = Path(backup_state_directory)

    def _source(self, manifest):
        validate(manifest, self.configuration())
        ensure(self.source.is_dir() and not self.source.is_symlink() and self.source.resolve() == self.source,
               "TOOL_SOURCE_INVALID")
        for name in FILES:
            ensure(content_digest(self.source / name) == manifest["files"][name], "TOOL_FILE_CHANGED")

    def _timers(self):
        for name in ("monkado-deploy.timer", "monkado-frontend.timer"):
            for field, expected in (("ActiveState", b"inactive\n"), ("UnitFileState", b"disabled\n")):
                ensure(self.native.run(["systemctl", "show", name, "-p", field, "--value"], timeout=15) == expected,
                       "AUTOMATIC_PUBLICATION_ACTIVE")

    def _pair(self):
        state = self.backend_store.load()
        ensure(state["phase"] in {"succeeded", "acknowledged"} and state["current"] is not None, "BACKEND_CHANGED")
        backend = state["current"]
        ensure(hosting.backend_revision(self.state) == backend["revision"], "BACKEND_CHANGED")
        revision = hosting.current_revision(self.publisher.releases)
        ensure(revision is not None, "FRONTEND_NOT_HEALTHY")
        frontend = self.publisher.verify(revision, backend["revision"])
        front_state = self.publisher.load()
        ensure(front_state["phase"] == "healthy" and front_state["error"] is None and
               not self.publisher.journal.exists() and not self.publisher.journal.is_symlink(), "FRONTEND_NOT_HEALTHY")
        self.native.verify_active(backend)
        return {"backend": backend, "frontend": frontend}

    def preflight(self, manifest, backup):
        """Require protected source, the fresh first snapshot and a healthy current pair."""
        self._source(manifest)
        self.approval(manifest)
        self._timers()
        ensure(not (self.state / "coordinated-transition.json").exists() and
               not (self.state / "coordinated-transition.json").is_symlink() and not self.gate.exists(),
               "RECOVERY_REQUIRED")
        validate_backup(self.backup_state(), backup, self.clock(), self.backup_started)
        previous = self._pair()
        old = backup_hashes(self.backup_parent / "monkado_backup")
        return {"pair": previous, "backupFiles": old, "backupStarted": self.backup_started.isoformat()}

    def validate_intent(self, intent):
        """Verify the original pair and fixed manifest without requiring live approvals."""
        validate_install_intent(intent, self.configuration())
        self._source(intent["manifest"])

    def revalidate_recovery(self, intent):
        """Refuse a stale or failed remote backup before resuming configuration effects."""
        self._timers()
        marker = self.backup_state_directory / "maintenance.json"
        ensure(not marker.is_symlink(), "BACKUP_RECOVERY_REQUIRED")
        if marker.exists():
            backup_command("recover", inherited=True)
            self.gate.verify()
            ensure(not marker.exists() and not marker.is_symlink(), "BACKUP_RECOVERY_REQUIRED")
        current = self.backup_state()
        started = datetime.fromisoformat(intent["backupStarted"])
        validate_backup(current, current.get("snapshotId"), self.clock(), started)
        ensure(self._pair() == intent["previousPair"], "PAIR_CHANGED")

    def stage(self, intent):
        self._timers()
        ensure(self._pair() == intent["previousPair"], "PAIR_CHANGED")
        stage_tool(self.source, intent["manifest"], self.root, self.settings)

    def install_guard(self, intent):
        stage_guard(self.source, intent["manifest"], self.systemd)
        self.native.run(["systemctl", "daemon-reload"], timeout=30)
        self.native.run(["systemctl", "start", "monkado-transition-guard.service"], timeout=30)
        self.native.run(["systemctl", "is-active", "--quiet", "monkado-transition-guard.service"], timeout=15)
        self.gate.verify()

    def close_gate(self):
        try:
            self.gate.close()
        except Exception:
            try:
                self.native.run(["systemctl", "stop", "caddy.service"], timeout=120)
            except Exception:
                raise DeploymentError("TRAFFIC_GATE_CLOSURE_UNCERTAIN") from None
            raise DeploymentError("TRAFFIC_GATE_UNAVAILABLE") from None

    def install_backup_code(self, intent):
        """Exchange whole backup packages under the same lock used by scheduled backups."""
        self.gate.verify()
        with backup_lock(self.backup_state_directory / "backup.lock"):
            activate_backup(self.source, intent["manifest"], self.backup_parent, intent["previousBackupFiles"])

    def verify_installed(self, intent):
        self.gate.verify()
        verify_installed(intent["manifest"], self.configuration(), self.root,
                         self.backup_parent / "monkado_backup", self.systemd / "monkado-transition-guard.service",
                         self.systemd / "caddy.service.requires/monkado-transition-guard.service")
        for name in BACKUP_FILES:
            target = self.root / "src/Operations.Coordinator/backup" / Path(name).name
            ensure(content_digest(target) == intent["manifest"]["files"][name], "TOOL_FILE_CHANGED")
        self.native.run(["systemctl", "is-active", "--quiet", "monkado-transition-guard.service"], timeout=15)

    def capture_installed_backup(self, intent):
        self.verify_installed(intent)
        return self.backup_capture(clock=self.clock, gate=self.gate, inherited=True)

    def verify_active_pair(self, intent):
        self.gate.verify()
        ensure(self._pair() == intent["previousPair"], "PAIR_CHANGED")
        state = self.backend_store.load()
        baseline = copy.deepcopy(state["baseline"])
        ensure(isinstance(baseline, dict) and baseline.get("frontend") is not None, "FRONTEND_BASELINE_REQUIRED")
        frontend = intent["previousPair"]["frontend"]
        baseline["frontend"] = frontend_contract.release_marker(frontend["revision"], frontend.get("googleEnabled", False))
        self.native.restore_baseline(baseline)
        ensure(self.native.smoke(intent["previousPair"]["backend"]) ==
               {"technical": True, "functional": True, "frontend": True}, "BACKEND_VERIFICATION_FAILED")
        self.gate.verify()

    def open_gate(self):
        self.gate.open()
