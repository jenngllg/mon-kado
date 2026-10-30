"""Check strict artifact binding and installed root-owned code."""

import copy
import os
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_integrity import BACKUP_FILES, COORDINATOR_FILES, create, digest, validate, verify_installed
from deploy_policy import DeploymentError


@unittest.skipUnless(hasattr(os, "geteuid") and os.geteuid() == 0, "requires root-owned Linux fixtures")
class CoordinatedIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "source"; self.source.mkdir()
        self.installed = self.root / "installed"; self.installed.mkdir()
        self.backup = self.root / "backup"; self.backup.mkdir()
        for name in COORDINATOR_FILES + BACKUP_FILES:
            source = self.source / name; source.parent.mkdir(parents=True, exist_ok=True)
            source.write_text("fixture " + name)
            target = self.installed / name if name in COORDINATOR_FILES else self.backup / Path(name).name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(source.read_bytes())
            target.chmod(0o755 if name == "deployments/production/monkado-coordinate" else 0o644)
        self.unit = self.root / "monkado-transition-guard.service"
        self.unit.write_bytes((self.source / "deployments/production/monkado-transition-guard.service").read_bytes())
        self.unit.chmod(0o644)
        self.requires = self.root / "requires"; self.requires.symlink_to(self.unit)
        self.manifest = create(self.source, "a" * 40, "b" * 64, "c" * 64)

    def verify(self, manifest=None):
        return verify_installed(manifest or self.manifest, "b" * 64, self.installed,
                                self.backup, self.unit, self.requires)

    def test_verify_when_all_installed_bytes_match_returns_manifest_digest(self):
        self.assertEqual(self.verify(), digest(self.manifest))

    def test_verify_when_file_or_unit_changes_fails_closed(self):
        for target in (self.installed / COORDINATOR_FILES[0], self.backup / Path(BACKUP_FILES[0]).name, self.unit):
            original = target.read_bytes()
            target.write_text("changed")
            with self.subTest(target=target), self.assertRaises(DeploymentError):
                self.verify()
            target.write_bytes(original)

    def test_verify_when_dependency_is_missing_or_file_is_linked_refuses(self):
        self.requires.unlink()
        with self.assertRaises(DeploymentError):
            self.verify()
        self.requires.symlink_to(self.unit)
        target = self.installed / COORDINATOR_FILES[0]
        target.unlink(); target.symlink_to(self.unit)
        with self.assertRaises(DeploymentError):
            self.verify()

    def test_verify_when_code_directory_is_writable_by_others_refuses(self):
        self.backup.chmod(0o777)
        with self.assertRaises(DeploymentError):
            self.verify()

    def test_validate_when_inventory_or_configuration_changes_refuses(self):
        for change in ({"revision": "develop"}, {"configurationHash": "0" * 64}, {"schemaVersion": True}):
            with self.subTest(change=change), self.assertRaises(DeploymentError):
                validate(self.manifest | change, "b" * 64)
        altered = copy.deepcopy(self.manifest); altered["files"]["extra"] = "a" * 64
        with self.assertRaises(DeploymentError):
            validate(altered, "b" * 64)


if __name__ == "__main__":
    unittest.main()
