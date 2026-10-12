"""Opt-in native systemd resource exercise with private files and no external network."""

from datetime import timedelta
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
import tempfile
import uuid
from unittest.mock import patch

def exercise():
    """Run real state/history/CLI code with synthetic observations inside the limited native service."""
    import resource
    import time
    import monitor_cli
    import monitor_runtime
    from test_monitor_policy import NOW, observation, snapshot

    class Collector:
        def collect(self, now, previous, options, frontend_enabled):
            value = observation()
            value["snapshots"] = {service: snapshot(service, now, 100) for service in ("api", "worker")}
            value["backup"]["lastRemoteCapture"] = now.isoformat()
            value["backup"]["lastIntegrityCheck"] = now.isoformat()
            value["checks"]["api"] = True
            return value, [], 0

    began = time.monotonic()
    sent = []
    root = Path(os.environ["MONKADO_NATIVE_FIXTURE"]) / "monitoring"
    monitor = monitor_runtime.Monitor(root, Collector(), lambda incidents, keys, now: sent.append(keys))
    configuration = {"schemaVersion": 1, "frontendEnabled": False, "notificationsEnabled": True, "thresholds": {}}
    # Virtual clock: no sleep, external request, database or actual e-mail.
    for minute in range(16):
        report = monitor.run(configuration, NOW + timedelta(minutes=minute))
    status = monitor_cli.status(root, NOW + timedelta(minutes=15))
    files = list(root.rglob("*"))
    output = {"cycles": 16, "rssKiB": resource.getrusage(resource.RUSAGE_SELF).ru_maxrss,
              "elapsedSeconds": time.monotonic() - began, "privateFiles": all(path.stat().st_mode & 0o077 == 0 for path in files),
              "storedBytes": sum(path.stat().st_size for path in files if path.is_file()),
              "incidentDetected": "api.unavailable" in report["openIncidents"],
              "notifications": len(sent), "monitorStale": status["monitorStale"]}
    print(json.dumps(output, sort_keys=True))


@unittest.skipUnless(os.environ.get("MONKADO_MONITOR_NATIVE_TESTS") == "1", "Explicit native systemd exercise only")
class MonitorNativeTests(unittest.TestCase):
    def test_real_native_oom_is_reported_once_as_a_new_incident(self):
        # Arrange
        import native_services
        import monitor_collect
        from test_monitor_policy import NOW
        self.assertEqual(0, os.geteuid())
        name = "monkado-monitor-probe-" + uuid.uuid4().hex + ".service"
        try:
            # Act: a real kernel memory limit kills this disposable process.
            result = subprocess.run(['systemd-run','--quiet','--wait','--unit='+name,
                                     '--property=MemoryMax=32M','--property=MemorySwapMax=0',
                                     '--property=PrivateNetwork=yes','--property=NoNewPrivileges=yes',
                                     sys.executable,'-c','payload = bytearray(128 * 1024 * 1024)'],
                                    capture_output=True, timeout=20, check=False)
            self.assertNotEqual(0,result.returncode)
            def runner(arguments):
                result = subprocess.run(arguments,capture_output=True,timeout=5,check=False)
                return result.returncode,result.stdout.decode()
            with patch.dict(native_services.UNITS,{key:name for key in native_services.UNITS}):
                collector = monitor_collect.Collector(runner=runner)
                observed,samples = collector.containers(NOW,[],600)
                repeated,_ = collector.containers(NOW,samples,600)
            # Assert: actual native evidence enters the retained history contract.
            self.assertEqual(4,len(samples))
            self.assertTrue(all(value['newOom'] and not value['running'] for value in observed.values()))
            self.assertTrue(all(not value['newOom'] for value in repeated.values()))
        finally:
            for action in ('stop','reset-failed'):
                subprocess.run(['systemctl',action,name],capture_output=True,timeout=15,check=False)

    def test_monitor_under_production_budget_uses_private_bounded_state_without_network(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
        self.assertEqual(0,os.geteuid())
        name = "monkado-monitor-budget-" + uuid.uuid4().hex + ".service"
        with tempfile.TemporaryDirectory(prefix='monkado-native-monitor-') as temporary:
            fixture = Path(temporary)
            fixture.chmod(0o700)
            os.chown(fixture,65534,65534)
            command = ['systemd-run','--quiet','--wait','--pipe','--unit='+name,
                       '--property=User=nobody','--property=Group=nogroup','--property=MemoryMax=128M',
                       '--property=MemorySwapMax=0','--property=CPUQuota=10%','--property=TasksMax=64',
                       '--property=PrivateNetwork=yes','--property=NoNewPrivileges=yes',
                       '--property=ProtectSystem=strict','--property=ReadWritePaths='+str(fixture),'--property=UMask=0077',
                       '--setenv=PYTHONDONTWRITEBYTECODE=1','--setenv=MONKADO_NATIVE_FIXTURE='+str(fixture),
                       '--setenv=PYTHONPATH='+str(root/'src/Operations.Monitoring')+':'+str(root/'tests/Operations.MonitoringTests'),
                       sys.executable,str(Path(__file__).resolve()),'--inside']
            # Act
            try:
                result = subprocess.run(command,capture_output=True,timeout=45,check=False)
                self.assertEqual(0,result.returncode,'NATIVE_MONITOR_BUDGET_EXERCISE_FAILED')
            finally:
                for action in ('stop','reset-failed'):
                    subprocess.run(['systemctl',action,name],capture_output=True,timeout=15,check=False)
        report = json.loads(result.stdout)
        # Assert
        self.assertEqual(16, report["cycles"])
        self.assertLess(report["rssKiB"], 128 * 1024)
        self.assertLess(report["elapsedSeconds"], 45)
        self.assertLess(report["storedBytes"], 32 * 1024 * 1024)
        self.assertTrue(report["privateFiles"])
        self.assertTrue(report["incidentDetected"])
        self.assertFalse(report["monitorStale"])
        self.assertEqual(1, report["notifications"])
        print(json.dumps(report, sort_keys=True))


if __name__ == "__main__":
    if sys.argv[1:] == ["--inside"]:
        exercise()
    else:
        unittest.main()
