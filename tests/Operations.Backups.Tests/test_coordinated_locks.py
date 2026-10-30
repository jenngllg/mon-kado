"""Verify inherited backup locks with native flock on disposable files."""

import fcntl
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from monkado_backup.operations import inherited_capture_locks, lock
from monkado_backup.policy import BackupError


@unittest.skipUnless(os.geteuid() == 0, "requires root-owned lock fixtures")
class CoordinatedLockTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name) / "lock"

    def test_inherited_lock_when_parent_holds_it_preserves_exclusion_after_child_closes(self):
        with self.path.open("a") as parent:
            fcntl.flock(parent, fcntl.LOCK_EX | fcntl.LOCK_NB)
            with lock(self.path, parent.fileno()):
                pass
            # The original open file description still holds the lock after the
            # inherited duplicate closes; an unrelated process cannot acquire it.
            code = "import fcntl,sys; f=open(sys.argv[1], 'a'); fcntl.flock(f, fcntl.LOCK_EX|fcntl.LOCK_NB)"
            blocked = subprocess.run([sys.executable, "-c", code, str(self.path)], capture_output=True, check=False)
            self.assertNotEqual(blocked.returncode, 0)
        acquired = subprocess.run([sys.executable, "-c", code, str(self.path)], capture_output=True, check=False)
        self.assertEqual(acquired.returncode, 0)

    def test_inherited_lock_when_descriptor_points_elsewhere_refuses(self):
        other = self.path.parent / "other"
        self.path.touch()
        with other.open("a") as stream, self.assertRaisesRegex(BackupError, "INHERITED_LOCK_INVALID"):
            with lock(self.path, stream.fileno()):
                self.fail("wrong descriptor was accepted")

    def test_capture_descriptor_pair_when_partial_or_unexpected_refuses(self):
        with patch.dict(os.environ, {}, clear=True):
            self.assertEqual(inherited_capture_locks(), (None, None))
        with patch.dict(os.environ, {"MONKADO_BACKUP_COORDINATION_FD": "8", "MONKADO_BACKUP_DEPLOYMENT_FD": "9"}, clear=True):
            self.assertEqual(inherited_capture_locks(), (8, 9))
        for value in ({"MONKADO_BACKUP_COORDINATION_FD": "8"},
                      {"MONKADO_BACKUP_COORDINATION_FD": "9", "MONKADO_BACKUP_DEPLOYMENT_FD": "8"}):
            with self.subTest(value=value), patch.dict(os.environ, value, clear=True), self.assertRaises(BackupError):
                inherited_capture_locks()


if __name__ == "__main__":
    unittest.main()
