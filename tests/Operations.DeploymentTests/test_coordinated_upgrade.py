"""Upgrade real root-owned tool files with simulated services and backup provider."""

import copy
from datetime import timedelta
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

from test_coordinated_policy import pair, NOW, SNAPSHOT, backup
from coordinated_integrity import BACKUP_FILES, COORDINATOR_FILES, CONFIG_UPGRADE_FILES, FILES, create, verify_installed
from coordinated_install_engine import UpgradeEngine
from coordinated_install_files import replace_reviewed
from coordinated_install_runtime import UpgradeRuntime
from coordinated_frontend import CoordinatedFrontend
from coordinated_store import InstallationStore
from deploy_policy import DeploymentError
from deploy_storage import atomic_write
from release_catalog import identifiers
from release_manifest import FILES as CONFIG_FILES, configuration_hash


@unittest.skipUnless(hasattr(os, "geteuid") and os.geteuid() == 0, "requires root-owned Linux fixtures")
class UpgradeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        base = Path(self.temporary.name)
        self.root, self.source, self.old_source, self.state, self.settings, self.systemd, self.backup_root = [
            base / name for name in ("installed", "source", "old-source", "state", "settings", "systemd", "backup")]
        for path in (self.root, self.source, self.old_source, self.state, self.settings, self.systemd, self.backup_root):
            path.mkdir(mode=0o700)
        for name in set(CONFIG_FILES) | set(FILES):
            for tree in (self.root, self.source, self.old_source):
                path = tree / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("original " + name)
                path.chmod(0o755 if name == "deployments/production/monkado-coordinate" else 0o644)
        for name in COORDINATOR_FILES + CONFIG_UPGRADE_FILES:
            if name != "deployments/production/monkado-transition-guard.service":
                (self.source / name).write_text("reviewed " + name)
        self.old_hash, self.new_hash = configuration_hash(self.root), configuration_hash(self.source)
        self.old_manifest = create(self.old_source, "a" * 40, self.old_hash, "1" * 64)
        for name in CONFIG_UPGRADE_FILES:
            self.old_manifest["files"].pop(name)
        self.manifest = create(self.source, "b" * 40, self.new_hash, "2" * 64)
        package = self.backup_root / "monkado_backup"
        package.mkdir()
        for name in BACKUP_FILES:
            target = package / Path(name).name
            target.write_bytes((self.old_source / name).read_bytes())
            target.chmod(0o644)
            mirror = self.root / "src/Operations.Coordinator/backup" / Path(name).name
            mirror.parent.mkdir(parents=True, exist_ok=True)
            mirror.write_bytes(target.read_bytes()); mirror.chmod(0o644)
        unit = self.systemd / "monkado-transition-guard.service"
        unit.write_bytes((self.source / "deployments/production/monkado-transition-guard.service").read_bytes())
        unit.chmod(0o644)
        requires = self.systemd / "caddy.service.requires"
        requires.mkdir(); (requires / unit.name).symlink_to(unit)
        self.manifest_path = self.settings / "coordinated-manifest.json"
        atomic_write(self.manifest_path, json.dumps(self.old_manifest).encode())
        self.previous = pair("c", "d", "123-1")
        self.previous["backend"]["configurationHash"] = self.old_hash
        self.previous["frontend"]["configurationHash"] = self.old_hash
        self.schema = b'{"openapi":"3.1.0"}'
        self.previous["proof"] = {"schemaVersion": 1, "frontendRevision": "d" * 40, "backendRevision": "c" * 40,
                                   "configurationHash": self.old_hash, "openapiSha256": hashlib.sha256(self.schema).hexdigest()}
        self.backend_state = {"phase": "succeeded", "current": copy.deepcopy(self.previous["backend"]), "baseline": {}}
        self.backend_store = Mock(); self.backend_store.load.side_effect = lambda: self.backend_state
        self.native = Mock()
        self.native.run.side_effect = lambda argv, **kwargs: b"inactive\n" if "ActiveState" in argv else b"disabled\n" if "UnitFileState" in argv else b""
        self.native.history.return_value = identifiers(self.previous["backend"]["migrationCatalog"])
        self.native.clients.return_value.request.return_value = (200, {}, self.schema)
        self.native.smoke.return_value = {"technical": True, "functional": True, "frontend": True}
        self.publisher = Mock()
        self.publisher.root = base / "frontend"; self.publisher.root.mkdir()
        self.publisher.releases = self.publisher.root / "releases"; self.publisher.releases.mkdir()
        (self.publisher.releases / ("d" * 40)).mkdir()
        (self.publisher.releases / "current").symlink_to("d" * 40)
        (self.state / "current.env").write_text("RELEASE_REVISION=" + "c" * 40 + "\n")
        self.gate = Mock(); self.gate.exists.return_value = False
        self.capture = Mock(return_value="3" * 64)
        (base / "backup-state").mkdir(mode=0o700)
        self.runtime = UpgradeRuntime(self.source, self.root, self.state, self.settings, self.backup_root, self.systemd,
            self.backend_store, self.native, self.publisher, self.gate, lambda: configuration_hash(self.root), Mock(), backup,
            NOW - timedelta(minutes=10), clock=lambda: NOW, backup_capture=self.capture, backup_state_directory=base / "backup-state")
        self.store = InstallationStore(self.state)
        self.rebaseline = patch("coordinated_install_runtime.rebaseline", side_effect=self.baseline)
        self.rebaseline.start(); self.addCleanup(self.rebaseline.stop)
        self.historical = patch.object(CoordinatedFrontend, "historical_pair", return_value=self.previous)
        self.historical.start(); self.addCleanup(self.historical.stop)
        self.original = patch.object(CoordinatedFrontend, "verify_original", return_value=self.previous["frontend"])
        self.original.start(); self.addCleanup(self.original.stop)

    def baseline(self, store, runtime, expected):
        self.assertEqual(self.new_hash, configuration_hash(self.root))
        self.backend_state["current"]["configurationHash"] = expected
        return self.backend_state

    def test_upgrade_authenticates_old_bytes_changes_only_reviewed_files_and_keeps_application_pair(self):
        original_revision = self.backend_state["current"]["revision"]
        result = UpgradeEngine(self.store, self.runtime).install(self.manifest, SNAPSHOT)
        self.assertEqual({"preInstallSnapshot": SNAPSHOT, "postInstallSnapshot": "3" * 64}, result)
        self.assertIsNone(self.store.load())
        self.assertEqual(original_revision, self.backend_state["current"]["revision"])
        self.assertEqual(self.new_hash, configuration_hash(self.root))
        verify_installed(self.manifest, self.new_hash, self.root, self.backup_root / "monkado_backup",
                         self.systemd / "monkado-transition-guard.service", self.systemd / "caddy.service.requires/monkado-transition-guard.service")
        self.gate.open.assert_called_once()
        self.native.start.assert_not_called()
        self.native.migrate.assert_not_called()

    def test_changed_source_uninventoried_configuration_and_unknown_old_bytes_are_rejected(self):
        name = CONFIG_UPGRADE_FILES[0]
        (self.source / name).write_text("unreviewed")
        with self.assertRaises(DeploymentError):
            UpgradeEngine(self.store, self.runtime).install(self.manifest, SNAPSHOT)
        self.assertIsNone(self.store.load())
        (self.source / name).write_text("reviewed " + name)
        other = next(item for item in CONFIG_FILES if item not in CONFIG_UPGRADE_FILES)
        (self.root / other).write_text("unknown configuration")
        with self.assertRaises(DeploymentError):
            UpgradeEngine(self.store, self.runtime).install(self.manifest, SNAPSHOT)
        self.gate.open.assert_not_called()

    def test_already_active_boot_guard_is_closed_before_verification_without_requiring_recovery(self):
        closed = False
        def close():
            nonlocal closed
            closed = True
        def verify():
            if not closed:
                raise DeploymentError("TRAFFIC_GATE_UNAVAILABLE")
        self.gate.close.side_effect = close
        self.gate.verify.side_effect = verify
        # systemctl start intentionally has no side effect for an active oneshot.
        UpgradeEngine(self.store, self.runtime).install(self.manifest, SNAPSHOT)
        self.assertIsNone(self.store.load())
        self.gate.open.assert_called_once()
        self.native.run.assert_any_call(["systemctl", "start", "monkado-transition-guard.service"], timeout=30)

    def test_interrupted_upgrade_reuses_exact_intent_and_rechecks_before_opening(self):
        self.capture.side_effect = SystemExit("lost backup acknowledgement")
        with self.assertRaises(SystemExit):
            UpgradeEngine(self.store, self.runtime).install(self.manifest, SNAPSHOT)
        self.assertIsNotNone(self.store.load())
        self.gate.open.assert_not_called()
        self.capture.side_effect = None
        UpgradeEngine(self.store, self.runtime).recover()
        self.assertIsNone(self.store.load())
        self.gate.open.assert_called_once()

    def test_partial_file_replacement_is_resumed_and_changed_backup_package_is_retained(self):
        name = "src/Operations.Backups/monkado_backup/capture.py"
        (self.source / name).write_text("reviewed capture")
        self.manifest = create(self.source, "b" * 40, self.new_hash, "2" * 64)
        calls = []
        def interrupt_after_write(*args):
            replace_reviewed(*args)
            calls.append(args[0])
            if len(calls) == 2:
                raise SystemExit("interrupted code replacement")
        with patch("coordinated_install_runtime.replace_reviewed", side_effect=interrupt_after_write), self.assertRaises(SystemExit):
            UpgradeEngine(self.store, self.runtime).install(self.manifest, SNAPSHOT)
        self.assertIsNotNone(self.store.load())
        self.gate.open.assert_not_called()
        UpgradeEngine(self.store, self.runtime).recover()
        self.assertIsNone(self.store.load())
        self.assertEqual("reviewed capture", (self.backup_root / "monkado_backup/capture.py").read_text())
        retained = self.backup_root / ("monkado_backup.coordinator-" + self.manifest["archiveSha256"][:16])
        self.assertEqual("original " + name, (retained / "capture.py").read_text())


if __name__ == "__main__":
    unittest.main()
