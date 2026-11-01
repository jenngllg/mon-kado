"""Explicit local Linux exercises create only disposable units with private memory limits."""

import hashlib
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch
import uuid

import native_services


@unittest.skipUnless(os.environ.get("MONKADO_NATIVE_SYSTEMD_TESTS") == "1", "Explicit native systemd exercise only")
class SystemdTests(unittest.TestCase):
    def setUp(self):
        self.assertEqual(0, os.geteuid())
        self.name = "monkado-native-probe-" + uuid.uuid4().hex + ".service"
        self.addCleanup(self.cleanup)

    def cleanup(self):
        for action in ("stop", "reset-failed"):
            subprocess.run(["systemctl", action, self.name], stdout=subprocess.DEVNULL,
                           stderr=subprocess.DEVNULL, timeout=15, check=False)

    @staticmethod
    def command(arguments):
        result = subprocess.run(arguments, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=15, check=True)
        return result.stdout

    def launch(self, program, wait=False):
        arguments = ["systemd-run", "--quiet", "--unit=" + self.name, "--property=Type=exec",
                     "--property=MemoryMax=32M", "--property=MemorySwapMax=0", "--property=TasksMax=16",
                     "--property=NoNewPrivileges=yes"]
        if wait:
            arguments.append("--wait")
        return subprocess.run([*arguments, sys.executable, "-c", program], stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL, timeout=20, check=False)

    def test_running_executable_timestamp_and_restarts_are_observable_without_process_secrets(self):
        self.assertEqual(0, self.launch("import signal; signal.pause()").returncode)
        with patch.dict(native_services.UNITS, {"api": self.name}):
            first = native_services.observe("api", self.command)
            self.command(["systemctl", "restart", self.name])
            second = native_services.observe("api", self.command)
        self.assertTrue(first["running"])
        self.assertEqual(hashlib.sha256(Path(sys.executable).read_bytes()).hexdigest(), first["image"])
        self.assertTrue(first["startedAt"].endswith("+00:00"))
        self.assertNotEqual(first["id"], second["id"])
        self.assertEqual(first["monitorId"], second["monitorId"])
        self.assertEqual(first["image"], second["image"])

    def test_actual_isolated_memory_failure_is_retained_as_native_oom_evidence(self):
        self.assertNotEqual(0, self.launch("payload = bytearray(128 * 1024 * 1024)", wait=True).returncode)
        with patch.dict(native_services.UNITS, {"api": self.name}):
            observed = native_services.observe("api", self.command)
        self.assertFalse(observed["running"])
        self.assertTrue(observed["oom"])
        self.assertIsNone(observed["image"])
