import tempfile
import runpy
import unittest
from pathlib import Path
from unittest.mock import patch

from cgroup import operate
from policy import MEMORY, PerformanceError


class CgroupTests(unittest.TestCase):
    def test_script_rejects_unknown_action(self):
        with patch("sys.argv", ["cgroup", "unknown", "mk816-012345abcdef"]):
            with self.assertRaisesRegex(PerformanceError, "INVALID_CGROUP_ACTION"):
                runpy.run_module("cgroup", run_name="__main__")

    def test_creation_checks_effective_shared_limits_and_counters(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "cgroup.subtree_control").write_text("cpu memory")
            original = Path.mkdir
            def kernel_files(path, *args, **kwargs):
                original(path, *args, **kwargs)
                for name, value in {"memory.current": "42", "memory.peak": "99",
                                    "memory.events": "oom_kill 0\n", "cpu.stat": "usage_usec 12\n"}.items():
                    (path / name).write_text(value)
            with patch.object(Path, "mkdir", kernel_files):
                result = operate("create", "mk816-012345abcdef", root)
            self.assertTrue(result["qualified"])
            self.assertEqual(MEMORY, result["memoryMax"])
            self.assertEqual(99, result["memoryPeak"])
            self.assertEqual("cpu memory", (root / "cgroup.subtree_control").read_text())
            with self.assertRaisesRegex(PerformanceError, "CGROUP_ALREADY_EXISTS"):
                operate("create", "mk816-012345abcdef", root)
            group = root / "mk816-012345abcdef"
            (group / "memory.max").write_text("12")
            self.assertFalse(operate("status", "mk816-012345abcdef", root)["qualified"])
            for item in group.iterdir():
                item.unlink()
            self.assertTrue(operate("remove", "mk816-012345abcdef", root)["removed"])

    def test_missing_controllers_and_unknown_actions_are_refused(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "cgroup.subtree_control").write_text("memory")
            with self.assertRaisesRegex(PerformanceError, "CGROUP_UNSUPPORTED"):
                operate("create", "mk816-012345abcdef", root)
            with self.assertRaisesRegex(PerformanceError, "INVALID_CGROUP_ACTION"):
                operate("erase", "mk816-012345abcdef", root)

    def test_systemd_missing_slice_and_idempotent_removal(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaisesRegex(PerformanceError, "CGROUP_MISSING"):
                operate("configure", "mk816-012345abcdef", root, "systemd")
            self.assertTrue(operate("remove", "mk816-012345abcdef", root)["removed"])
            with patch("sys.argv", ["cgroup", "status", "mk816-012345abcdef", "unsupported"]):
                with self.assertRaisesRegex(PerformanceError, "INVALID_CGROUP_DRIVER"):
                    runpy.run_module("cgroup", run_name="__main__")
