"""Opt-in disposable resource exercise: no network, host state, Docker socket or provider secrets."""

from datetime import timedelta
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest

IMAGE = "python:3.13-slim@sha256:8d9d0b8bcf6506481eae4907c18f5e3e7902e629f5f6d684f9e7c32e85e3ddf0"


def exercise():
    """Run real state/history/CLI code with synthetic observations inside the limited container."""
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
    root = Path("/tmp/monitoring")
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


@unittest.skipUnless(os.environ.get("MONKADO_MONITOR_DOCKER_TESTS") == "1", "Explicit isolated Docker exercise only")
class MonitorContainerTests(unittest.TestCase):
    def test_real_container_oom_is_reported_as_a_new_incident(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
        sys.path.insert(0, str(root / "src/Operations.Monitoring"))
        import monitor_collect
        from test_monitor_policy import NOW
        created = subprocess.run(["docker", "create", "--network", "none", "--memory", "32m", "--memory-swap", "32m",
                                  "--pids-limit", "32", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                                  "--read-only", IMAGE, "python", "-c",
                                  "payload = bytearray(128 * 1024 * 1024)\n"
                                  "payload[::4096] = b'x' * (len(payload) // 4096)"],
                                 capture_output=True, check=True, timeout=15)
        container = created.stdout.decode().strip()
        self.assertRegex(container, r"^[0-9a-f]{64}$")
        try:
            # Act
            subprocess.run(["docker", "start", "--attach", container], capture_output=True, check=False, timeout=30)
            def runner(arguments):
                # Only substitute the owned disposable container; inspect's fixed field allowlist is unchanged.
                inspected = subprocess.run(arguments[:-1] + [container], capture_output=True, check=True, timeout=5)
                return inspected.returncode, inspected.stdout.decode()
            collector = monitor_collect.Collector(runner=runner)
            observed, samples = collector.containers(NOW, [], 600)
            # Assert
            self.assertEqual(4, len(samples))
            self.assertTrue(all(value["newOom"] for value in observed.values()))
            self.assertTrue(all(not value["running"] for value in observed.values()))
            repeated, _ = collector.containers(NOW, samples, 600)
            self.assertTrue(all(not value["newOom"] for value in repeated.values()))
        finally:
            subprocess.run(["docker", "rm", "--force", container], capture_output=True, check=True, timeout=15)

    def test_monitor_under_production_budget_uses_private_bounded_state_without_network(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
        command = ["docker", "run", "--rm", "--network", "none", "--cpus", "0.1", "--memory", "128m",
                   "--memory-swap", "128m", "--pids-limit", "64", "--cap-drop", "ALL",
                   "--security-opt", "no-new-privileges", "--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,size=40m",
                   "--mount", "type=bind,source=" + str(root) + ",target=/source,readonly",
                   "-e", "PYTHONDONTWRITEBYTECODE=1",
                   "-e", "PYTHONPATH=/source/src/Operations.Monitoring:/source/tests/Operations.MonitoringTests",
                   IMAGE, "python", "/source/tests/Operations.MonitoringTests/test_monitor_container.py", "--inside"]
        # Act
        result = subprocess.run(command, capture_output=True, check=True, timeout=45)
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
