"""Verify first-install staging and recoverable backup-code exchange on Linux."""

import os
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_install_files import activate_backup, backup_hashes, stage_guard, stage_tool
from coordinated_integrity import BACKUP_FILES, BACKUP_MODULES, FILES, create
from deploy_policy import DeploymentError


@unittest.skipUnless(hasattr(os, "geteuid") and os.geteuid() == 0, "requires root-owned Linux fixture files")
class InstallationFilesTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        base = Path(self.temporary.name)
        self.source = base / "source"; self.source.mkdir()
        self.root = base / "installed"; self.root.mkdir()
        self.settings = base / "settings"; self.settings.mkdir(mode=0o700)
        self.backup_parent = base / "backup"; self.backup_parent.mkdir()
        self.systemd = base / "systemd"; self.systemd.mkdir()
        self.current = self.backup_parent / "monkado_backup"; self.current.mkdir()
        for name in FILES:
            target = self.source / name; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("new " + name)
        for name in BACKUP_MODULES:
            target = self.current / name; target.write_text("old " + name); target.chmod(0o644)
        self.previous = backup_hashes(self.current)
        self.manifest = create(self.source, "a" * 40, "b" * 64, "c" * 64)
        self.historical = self.root / "src/Operations.Backups/monkado_backup/policy.py"
        self.historical.parent.mkdir(parents=True)
        self.historical.write_text("preserve historical source")

    def test_stage_when_repeated_preserves_existing_historical_sources(self):
        stage_tool(self.source, self.manifest, self.root, self.settings)
        stage_tool(self.source, self.manifest, self.root, self.settings)
        self.assertEqual(self.historical.read_text(), "preserve historical source")
        self.assertTrue((self.settings / "coordinated-manifest.json").is_file())
        self.assertTrue((self.root / "src/Operations.Coordinator/backup/policy.py").is_file())

    def test_stage_when_new_path_contains_unrelated_data_never_overwrites_it(self):
        target = self.root / "src/Operations.Deployment/coordinated_backend.py"
        target.parent.mkdir(parents=True); target.write_text("user data")
        with self.assertRaises(DeploymentError):
            stage_tool(self.source, self.manifest, self.root, self.settings)
        self.assertEqual(target.read_text(), "user data")

    def test_activate_when_repeated_keeps_original_package_for_recovery(self):
        activate_backup(self.source, self.manifest, self.backup_parent, self.previous)
        wanted = {Path(name).name: self.manifest["files"][name] for name in BACKUP_FILES}
        self.assertEqual(backup_hashes(self.current), wanted)
        saved = self.backup_parent / ("monkado_backup.coordinator-" + "c" * 16)
        self.assertEqual(backup_hashes(saved), self.previous)
        activate_backup(self.source, self.manifest, self.backup_parent, self.previous)
        self.assertEqual(backup_hashes(self.current), wanted)
        self.assertEqual(backup_hashes(saved), self.previous)

    def test_activate_when_existing_backup_changes_refuses_swap(self):
        (self.current / "policy.py").write_text("unexpected")
        with self.assertRaisesRegex(DeploymentError, "BACKUP_CODE_CHANGED"):
            activate_backup(self.source, self.manifest, self.backup_parent, self.previous)
        self.assertEqual((self.current / "policy.py").read_text(), "unexpected")

    def test_guard_when_repeated_keeps_exact_unit_and_dependency(self):
        stage_guard(self.source, self.manifest, self.systemd)
        stage_guard(self.source, self.manifest, self.systemd)
        unit = self.systemd / "monkado-transition-guard.service"
        link = self.systemd / "caddy.service.requires/monkado-transition-guard.service"
        self.assertTrue(link.is_symlink())
        self.assertEqual(link.resolve(), unit)

    def test_guard_when_dependency_points_elsewhere_refuses(self):
        directory = self.systemd / "caddy.service.requires"; directory.mkdir()
        link = directory / "monkado-transition-guard.service"; link.symlink_to(self.historical)
        with self.assertRaises(DeploymentError):
            stage_guard(self.source, self.manifest, self.systemd)
        self.assertEqual(link.resolve(), self.historical)


if __name__ == "__main__":
    unittest.main()
