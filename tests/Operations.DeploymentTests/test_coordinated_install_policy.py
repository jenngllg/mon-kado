"""Validate pinned installation intent before every filesystem effect."""

from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Deployment", "src/Operations.Frontend", "deployments/production", "tests/Operations.DeploymentTests"):
    sys.path.insert(0, str(ROOT / path))
from coordinated_install_policy import validate_install_intent
from coordinated_integrity import BACKUP_MODULES, FILES
from deploy_policy import DeploymentError
from test_coordinated_policy import CONFIG, pair


class InstallationPolicyTests(unittest.TestCase):
    def test_validate_when_manifest_snapshots_or_backup_inventory_are_changed_refuses(self):
        manifest = {"schemaVersion": 1, "revision": "a" * 40, "configurationHash": CONFIG,
                    "archiveSha256": "c" * 64, "files": {name: "d" * 64 for name in FILES}}
        intent = {"version": 1, "phase": "prepared", "manifest": manifest, "backup": "e" * 64,
                  "previousBackupFiles": {name: "f" * 64 for name in BACKUP_MODULES}, "previousPair": pair("a", "e", "123-1"),
                  "backupStarted": "2026-10-30T12:00:00+00:00", "postInstallBackup": None}
        validate_install_intent(intent, CONFIG)
        for change in ({"version": True}, {"phase": "unknown"}, {"backup": "latest"},
                       {"previousBackupFiles": {}}, {"phase": "committed"},
                       {"postInstallBackup": "e" * 64}, {"backupStarted": "2026-10-30T12:00:00"}, {"extra": 1}):
            with self.subTest(change=change), self.assertRaises(DeploymentError):
                validate_install_intent(intent | change, CONFIG)
        validate_install_intent(intent | {"phase": "committed", "postInstallBackup": "0" * 64}, CONFIG)


if __name__ == "__main__":
    unittest.main()
