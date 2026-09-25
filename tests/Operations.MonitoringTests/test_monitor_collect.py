"""Read-only host collection tests with deterministic clocks and substituted providers."""

from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, Mock

import monitor_collect as collect
import monitor_policy as policy
from monitor_storage import atomic_json
from test_monitor_policy import NOW, snapshot


class CollectorTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.cgroup = self.root / "memory.events"
        self.cgroup.write_text("oom 0\noom_kill 2\n")
        self.active = 3
        self.container = {"id": "a" * 64, "running": True, "oom": False, "restarts": 0}
        self.calls = []
        self.collector = collect.Collector(self.root, self.cgroup, self.run_command, self.probe)
        backup = self.root / "monkado-backup"
        backup.mkdir()
        deployment = self.root / "monkado-deployment"
        deployment.mkdir()
        atomic_json(deployment / "status.json", {"schemaVersion": 1, "phase": "succeeded", "error": None})
        atomic_json(backup / "status.json", {"lastCapture": NOW.isoformat(), "lastRemoteCapture": NOW.isoformat(),
                                            "lastIntegrityCheck": NOW.isoformat(), "error": None})
        for service in ("api", "worker"):
            folder = self.root / "monkado-observability" / service
            folder.mkdir(parents=True)
            atomic_json(folder / "snapshot.json", snapshot(service))

    def run_command(self, arguments):
        self.calls.append(arguments)
        if arguments[0] == "systemctl":
            if arguments[1] == "show":
                return 0, "ActiveState=inactive\nResult=success\n"
            return self.active, ""
        return 0, json.dumps(self.container)

    def probe(self, service, path):
        return False, (NOW + timedelta(days=90)).isoformat()

    def test_collect_reads_only_fixed_technical_fields(self):
        # Arrange / Act
        observed, samples, oom = self.collector.collect(NOW, {"oomKills": 1}, policy.DEFAULTS, True)
        # Assert
        self.assertEqual(2, oom)
        self.assertTrue(observed["newHostOom"])
        self.assertEqual(4, len(samples))
        self.assertEqual({"api": False, "database": False, "frontend": False}, observed["checks"])
        self.assertFalse(observed["backup"]["missedScheduledCapture"])
        self.assertTrue(all(".Config" not in str(call) for call in self.calls))

    def test_frontend_transition_is_separate_from_backend_maintenance(self):
        # Arrange
        directory = self.root / "monkado-frontend"
        directory.mkdir()
        marker = directory / "transition.json"
        marker.write_text("PRIVATE_CONTENT_NOT_READ")
        os.utime(marker, (NOW.timestamp(), NOW.timestamp()))
        # Act
        observed, _, _ = self.collector.collect(NOW, {}, policy.DEFAULTS, True)
        # Assert
        self.assertEqual([NOW.isoformat()], observed["frontendMaintenance"])
        self.assertEqual([], observed["maintenance"])
        self.assertNotIn("PRIVATE", json.dumps(observed))

    def test_frontend_requires_both_page_and_release_even_when_one_probe_fails(self):
        # Arrange / Act / Assert
        for failing_path in ("/", "/release.json", None):
            for raises in (False, True):
                calls = []
                def probe(service, path):
                    calls.append((service, path))
                    bad = service == "frontend" and path == failing_path
                    if bad and raises:
                        raise OSError("simulated unavailable page")
                    return bad, (NOW + timedelta(days=90)).isoformat()
                self.collector.probe = probe
                with self.subTest(failing_path=failing_path, raises=raises):
                    observed, _, _ = self.collector.collect(NOW, {}, policy.DEFAULTS, True)
                    self.assertEqual(failing_path is not None, observed["checks"]["frontend"])
                    self.assertEqual([("frontend", "/"), ("frontend", "/release.json")],
                                     [call for call in calls if call[0] == "frontend"])

    def test_disabled_frontend_does_not_probe_page_or_release(self):
        # Arrange
        probe = Mock(side_effect=self.probe)
        self.collector.probe = probe
        # Act
        self.collector.collect(NOW, {}, policy.DEFAULTS, False)
        # Assert
        self.assertEqual([("api", "/liveness"), ("api", "/readiness")],
                         [call.args for call in probe.call_args_list])

    def test_missing_or_corrupt_evidence_is_unknown_not_healthy(self):
        # Arrange
        self.cgroup.unlink()
        (self.root / "monkado-backup/status.json").unlink()
        (self.root / "monkado-observability/api/snapshot.json").write_text("{}")
        self.collector.runner = lambda arguments: (1, "secret diagnostic")
        def fail(service, path):
            raise OSError("secret diagnostic")
        self.collector.probe = fail
        # Act
        observed, samples, oom = self.collector.collect(NOW, {}, policy.DEFAULTS, False)
        # Assert
        self.assertIsNone(oom)
        self.assertIsNone(observed["newHostOom"])
        self.assertIsNone(observed["backup"])
        self.assertIsNone(observed["snapshots"]["api"])
        self.assertTrue(all(item is None for item in observed["containers"].values()))
        self.assertEqual([], samples)
        self.assertNotIn("frontend", observed["checks"])
        self.assertNotIn("secret", json.dumps(observed))

    def test_restarts_and_oom_compare_same_container_within_window(self):
        # Arrange
        old = {"service": "api", "id": "a" * 64, "restarts": 1, "oom": False,
               "createdAt": (NOW - timedelta(minutes=5)).isoformat()}
        self.container.update(restarts=4, oom=True)
        # Act
        observed, samples = self.collector.containers(NOW, [old], 600)
        # Assert
        self.assertEqual(3, observed["api"]["recentRestarts"])
        self.assertTrue(observed["api"]["newOom"])
        observed, _ = self.collector.containers(NOW, samples, 600)
        self.assertFalse(observed["api"]["newOom"])
        self.assertEqual(0, observed["api"]["recentRestarts"])

    def test_backup_failure_waits_for_explicit_transfer_attempts_to_finish(self):
        # Arrange
        atomic_json(self.root / "monkado-backup/status.json", {"error": "OPERATION_FAILED"})
        self.active = 0
        # Act / Assert
        self.assertFalse(self.collector.backup(NOW)["terminalFailure"])
        self.active = 3
        self.assertTrue(self.collector.backup(NOW)["terminalFailure"])
        self.assertTrue(self.collector.backup(NOW)["missedScheduledCapture"])

    def test_paris_schedule_before_grace_uses_previous_day_across_dst(self):
        # Arrange / Act / Assert
        for now in (datetime(2026, 3, 29, 1, 10, tzinfo=timezone.utc),
                    datetime(2026, 10, 25, 2, 10, tzinfo=timezone.utc)):
            captured = now - timedelta(hours=23)
            atomic_json(self.root / "monkado-backup/status.json", {"lastCapture": captured.isoformat()})
            with self.subTest(now=now):
                self.assertFalse(self.collector.backup(now)["missedScheduledCapture"])
                self.assertTrue(self.collector.backup(now + timedelta(minutes=6))["missedScheduledCapture"])

    def test_maintenance_reads_mtime_without_reading_secret_marker_contents(self):
        # Arrange
        marker = self.root / "monkado-backup/maintenance.json"
        marker.write_text("must not be read")
        os.utime(marker, (NOW.timestamp(), NOW.timestamp()))
        # Act
        observed, _, _ = self.collector.collect(NOW, {}, policy.DEFAULTS, False)
        # Assert
        self.assertEqual([NOW.isoformat()], observed["maintenance"])


class TransportTests(unittest.TestCase):
    def test_frontend_html_page_uses_http_status_without_parsing_release_json(self):
        # Arrange / Act / Assert
        for status in (200, 404, 503):
            connection = Mock()
            connection.sock.getpeercert.return_value = {"notAfter": "Dec 21 12:00:00 2026 GMT"}
            connection.getresponse.return_value.status = status
            connection.getresponse.return_value.read.return_value = b"<!doctype html><html></html>"
            with self.subTest(status=status), patch.object(collect.http.client, "HTTPSConnection", return_value=connection):
                bad, _ = collect.https("frontend", "/")
                self.assertEqual(status != 200, bad)
                connection.request.assert_called_once_with("GET", "/", headers={"User-Agent": "MonKado-local-monitoring"})
                connection.close.assert_called_once()

    def test_command_bounds_output_and_has_no_retry(self):
        # Arrange
        process = Mock(returncode=1, stdout=b"technical")
        with patch.object(collect.subprocess, "run", return_value=process) as run:
            # Act
            result = collect.command(["fixed"])
            # Assert
            self.assertEqual((1, "technical"), result)
            run.assert_called_once_with(["fixed"], capture_output=True, timeout=2, check=False)

    def test_https_verifies_tls_and_closes_connection_for_all_outcomes(self):
        # Arrange / Act / Assert
        for service, status, body, bad in (
                ("api", 200, b"ok", False), ("api", 503, b"unavailable", True),
                ("frontend", 200, json.dumps({"revision": "a" * 40, "apiOrigin": "https://api.monkado.fr", "googleEnabled": False}).encode(), False),
                ("frontend", 200, json.dumps({"revision": "a" * 40, "apiOrigin": "https://api.monkado.fr", "googleEnabled": True}).encode(), False),
                ("frontend", 200, json.dumps({"revision": "a" * 40, "apiOrigin": "https://api.monkado.fr", "googleEnabled": 1}).encode(), True),
                ("frontend", 200, b"{}", True)):
            connection = Mock()
            connection.sock.getpeercert.return_value = {"notAfter": "Dec 21 12:00:00 2026 GMT"}
            connection.getresponse.return_value.status = status
            connection.getresponse.return_value.read.return_value = body
            with self.subTest(service=service, status=status), patch.object(collect.http.client, "HTTPSConnection", return_value=connection) as factory:
                result, expiry = collect.https(service, "/release.json" if service == "frontend" else "/liveness")
                self.assertEqual(bad, result)
                self.assertTrue(factory.call_args.kwargs["context"].check_hostname)
                connection.close.assert_called_once()
        with self.assertRaises(ValueError):
            collect.https("untrusted", "/liveness")
