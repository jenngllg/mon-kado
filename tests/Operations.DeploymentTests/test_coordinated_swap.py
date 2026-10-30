"""Exercise atomic directory exchange on a disposable native Linux filesystem."""

import os
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_swap import exchange
from deploy_policy import DeploymentError


@unittest.skipUnless(hasattr(os, "geteuid") and os.geteuid() == 0, "requires root-owned Linux fixture files")
class CoordinatedSwapTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.current, self.staged = self.root / "current", self.root / "staged"
        self.current.mkdir(mode=0o755); self.staged.mkdir(mode=0o755)
        (self.current / "marker").write_text("old")
        (self.staged / "marker").write_text("new")

    def test_exchange_when_both_directories_are_safe_swaps_contents_atomically(self):
        exchange(self.current, self.staged)
        self.assertEqual((self.current / "marker").read_text(), "new")
        self.assertEqual((self.staged / "marker").read_text(), "old")
        exchange(self.current, self.staged)
        self.assertEqual((self.current / "marker").read_text(), "old")

    def test_exchange_when_a_directory_is_writable_by_others_refuses(self):
        self.staged.chmod(0o777)
        with self.assertRaises(DeploymentError):
            exchange(self.current, self.staged)
        self.assertEqual((self.current / "marker").read_text(), "old")

    def test_exchange_when_a_path_is_a_symlink_refuses(self):
        alias = self.root / "alias"; alias.symlink_to(self.staged, target_is_directory=True)
        with self.assertRaises(DeploymentError):
            exchange(self.current, alias)
        self.assertEqual((self.current / "marker").read_text(), "old")


if __name__ == "__main__":
    unittest.main()
