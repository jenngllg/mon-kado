"""Exercise the actual Linux locks and inherited descriptors on a private fixture."""

import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Deployment", "src/Operations.Native", "src/Operations.Backups", "deployments/production"):
    sys.path.insert(0, str(ROOT / path))
from coordinated_locks import locks
from deploy_policy import DeploymentError
from monkado_backup.operations import lock as backup_lock


@unittest.skipUnless(hasattr(os, "geteuid") and os.geteuid() == 0, "requires root-owned Linux fixture files")
class CoordinatedLocksTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)

    def test_locks_when_inherited_backup_finishes_still_block_another_deployment(self):
        command = "import fcntl,sys; f=open(sys.argv[1], 'a'); fcntl.flock(f, fcntl.LOCK_EX|fcntl.LOCK_NB)"
        with locks(self.directory):
            with backup_lock(self.directory / "backup-coordination.lock", 8), backup_lock(self.directory / "deploy.lock", 9):
                pass
            for name in ("backup-coordination.lock", "deploy.lock"):
                result = subprocess.run([sys.executable, "-c", command, str(self.directory / name)],
                                        capture_output=True, check=False)
                self.assertNotEqual(result.returncode, 0)
        for name in ("backup-coordination.lock", "deploy.lock"):
            result = subprocess.run([sys.executable, "-c", command, str(self.directory / name)],
                                    capture_output=True, check=False)
            self.assertEqual(result.returncode, 0)

    def test_locks_when_path_is_symlink_refuse_before_acquisition(self):
        target = self.directory / "target"; target.touch(mode=0o600)
        (self.directory / "backup-coordination.lock").symlink_to(target)
        with self.assertRaises(OSError):
            with locks(self.directory):
                self.fail("linked lock was accepted")

    def test_locks_when_existing_file_is_not_private_refuse(self):
        target = self.directory / "backup-coordination.lock"; target.touch(mode=0o644); target.chmod(0o644)
        with self.assertRaisesRegex(DeploymentError, "UNSAFE_DEPLOYMENT_LOCK"):
            with locks(self.directory):
                self.fail("public lock was accepted")


if __name__ == "__main__":
    unittest.main()
