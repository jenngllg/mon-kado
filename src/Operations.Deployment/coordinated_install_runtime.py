"""Native effects for first installation of the reviewed coordination tools."""

import copy
import hashlib
import json
import tempfile
from datetime import datetime, timezone
from pathlib import Path

import frontend_runtime as hosting
import frontend_contract
from coordinated_backup import capture, run as backup_command
from coordinated_install_files import activate_backup, backup_hashes, replace_reviewed, stage_guard, stage_tool
from coordinated_install_policy import validate_install_intent, validate_upgrade_intent
from coordinated_integrity import BACKUP_FILES, CONFIG_UPGRADE_FILES, COORDINATOR_FILES, FILES, content_digest, validate, verify_installed, read_manifest
from coordinated_policy import validate_backup
from deploy_policy import DeploymentError, ensure
from monkado_backup.operations import lock as backup_lock
from deploy_cli import rebaseline
from deploy_storage import private_read
from release_catalog import identifiers
import release_manifest
from coordinated_frontend import CoordinatedFrontend


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

class UpgradeRuntime(InstallationRuntime):
    """Upgrade authenticated tool bytes and two reviewed configuration modules.

    This is a forward installation: the previous application keeps running,
    and the original frontend approval is never rewritten or re-bound.
    """

    def _source(self, manifest):
        validate(manifest, manifest["configurationHash"])
        ensure(set(manifest["files"]) == set(FILES), "TOOL_SOURCE_INVALID")
        for name in FILES:
            ensure(content_digest(self.source / name) == manifest["files"][name], "TOOL_FILE_CHANGED")
        # Hash the proposed configuration with the standard hash function.
        # Only the explicitly inventoried operational files may change.
        with tempfile.TemporaryDirectory(prefix="reviewed-configuration-") as temporary:
            shadow = Path(temporary)
            for name in release_manifest.FILES:
                source = self.source if name in CONFIG_UPGRADE_FILES else self.root
                content_digest(source / name)
                target = shadow / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((source / name).read_bytes())
            ensure(release_manifest.configuration_hash(shadow) == manifest["configurationHash"], "CONFIGURATION_CHANGED")

    def _verify_pair(self, pair, configurations):
        state = self.backend_store.load()
        ensure(state["phase"] in {"succeeded", "acknowledged"} and state["current"] is not None, "BACKEND_CHANGED")
        expected = pair["backend"]
        actual = state["current"]
        ensure(actual["configurationHash"] in configurations and
               actual | {"configurationHash": expected["configurationHash"]} == expected, "BACKEND_CHANGED")
        ensure(hosting.backend_revision(self.state) == expected["revision"] and
               hosting.current_revision(self.publisher.releases) == pair["frontend"]["revision"], "PAIR_CHANGED")
        ensure(self.native.history() == identifiers(expected["migrationCatalog"]), "DATABASE_HISTORY_DIVERGED")
        self.native.verify_active(expected)
        CoordinatedFrontend(self.publisher, self.gate).verify_original(pair)
        self.publisher.health(pair["frontend"]["revision"], self.publisher.releases / pair["frontend"]["revision"],
                              pair["frontend"].get("googleEnabled", False))
        status, _, body = self.native.clients().request("GET", "/openapi/v1.json")
        ensure(status == 200 and hashlib.sha256(body).hexdigest() == pair["proof"]["openapiSha256"], "BACKEND_CONTRACT_CHANGED")
        return actual

    def preflight(self, manifest, backup):
        self._source(manifest)
        self.approval(manifest)
        self._timers()
        ensure(not (self.state / "coordinated-transition.json").exists() and not self.gate.exists(), "RECOVERY_REQUIRED")
        validate_backup(self.backup_state(), backup, self.clock(), self.backup_started)
        previous = read_manifest(self.settings / "coordinated-manifest.json")
        verify_installed(previous, self.configuration(), self.root, self.backup_parent / "monkado_backup",
                         self.systemd / "monkado-transition-guard.service",
                         self.systemd / "caddy.service.requires/monkado-transition-guard.service")
        backend = self.backend_store.load()["current"]
        pair = CoordinatedFrontend(self.publisher, self.gate).historical_pair(backend)
        self._verify_pair(pair, {previous["configurationHash"]})
        return {"manifest": previous, "pair": pair, "backupFiles": backup_hashes(self.backup_parent / "monkado_backup"),
                "files": {name: content_digest(self.root / name) for name in CONFIG_UPGRADE_FILES},
                "backupStarted": self.backup_started.isoformat()}

    def validate_intent(self, intent):
        validate_upgrade_intent(intent)
        self._source(intent["manifest"])

    def revalidate_recovery(self, intent):
        self._timers()
        marker = self.backup_state_directory / "maintenance.json"
        ensure(not marker.is_symlink(), "BACKUP_RECOVERY_REQUIRED")
        if marker.exists():
            backup_command("recover", inherited=True)
        # A recorded installation can be resumed without trusting a new remote
        # approval or a newer deployment. Its original backup remains retained.
        self.validate_intent(intent)
        self._verify_pair(intent["previousPair"], {intent["previousManifest"]["configurationHash"], intent["manifest"]["configurationHash"]})

    def stage(self, intent):
        self._timers()
        self.validate_intent(intent)
        self._verify_pair(intent["previousPair"], {intent["previousManifest"]["configurationHash"], intent["manifest"]["configurationHash"]})

    def install_guard(self, intent):
        # An active RemainAfterExit guard does not execute again on start.
        # Close explicitly before checking it, including on the first attempt.
        self.close_gate()
        super().install_guard(intent)

    def install_backup_code(self, intent):
        self.gate.verify()
        source, previous = intent["manifest"], intent["previousManifest"]
        with backup_lock(self.backup_state_directory / "backup.lock"):
            for name in COORDINATOR_FILES + CONFIG_UPGRADE_FILES + BACKUP_FILES:
                target = self.root / name
                if name in BACKUP_FILES:
                    target = self.root / "src/Operations.Coordinator/backup" / Path(name).name
                old = intent["previousFiles"][name] if name in CONFIG_UPGRADE_FILES else previous["files"][name]
                mode = 0o755 if name == "deployments/production/monkado-coordinate" else 0o644
                replace_reviewed(target, (self.source / name).read_bytes(), old, source["files"][name], mode)
            activate_backup(self.source, source, self.backup_parent, intent["previousBackupFiles"])
            ensure(self.configuration() == source["configurationHash"], "CONFIGURATION_CHANGED")
            path = self.settings / "coordinated-manifest.json"
            raw = private_read(path, 65536)
            ensure(json.loads(raw) in (previous, source), "TOOL_APPROVAL_CHANGED")
            content = json.dumps(source, sort_keys=True, separators=(",", ":")).encode()
            replace_reviewed(path, content, hashlib.sha256(raw).hexdigest(), hashlib.sha256(content).hexdigest(), 0o600)
            rebaseline(self.backend_store, self.native, source["configurationHash"])

    def verify_active_pair(self, intent):
        self.gate.verify()
        current = self._verify_pair(intent["previousPair"], {intent["manifest"]["configurationHash"]})
        state = self.backend_store.load()
        self.native.restore_baseline(state["baseline"])
        ensure(self.native.smoke(current) == {"technical": True, "functional": True, "frontend": True}, "BACKEND_VERIFICATION_FAILED")
        self.gate.verify()


class OnlineUpgradeRuntime(UpgradeRuntime):
    """Upgrade reviewed operational files without stopping the serving API or proxy."""

    def close_gate(self):
        """Keep service available; retain durable installation intent on any failure."""
        self.gate.verify()

    def open_gate(self):
        self.gate.verify()

    def install_backup_code(self, intent):
        """Reload the serving proxy only when its reviewed configuration actually changes."""
        proxy = "deployments/caddy/Caddyfile"
        proxy_changed = any(intent["previousFiles"][name] != intent["manifest"]["files"][name]
                            for name in (proxy, "deployments/frontend/frontend.caddy"))
        if proxy_changed:
            from deploy_runtime import OnlineRuntime
            OnlineRuntime(root=self.root, state=self.state, settings=self.settings).route(7000)
        super().install_backup_code(intent)
        if proxy_changed:
            self.native.run(["systemd-run", "--quiet", "--wait", "--pipe", "--collect",
                             "--unit=mon-kado-online-proxy-setup", "--property=EnvironmentFile=" + str(self.settings / "caddy.env"),
                             "--", "/usr/bin/caddy", "reload", "--config", str(self.root / "deployments/caddy/Caddyfile"), "--adapter", "caddyfile"], timeout=30)
