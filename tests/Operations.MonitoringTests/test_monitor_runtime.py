"""Exercise durable incident state with an isolated filesystem and no external provider."""

import copy
from datetime import timedelta
import fcntl
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, Mock

import monitor_runtime as runtime
from monitor_collect import Collector
from monitor_storage import atomic_json, read_json, append_history
from test_monitor_policy import NOW, observation, snapshot


class FakeCollector:
    def __init__(self):
        self.value = observation()

    def collect(self, now, previous, options, frontend_enabled):
        value = copy.deepcopy(self.value)
        for snapshot in value["snapshots"].values():
            snapshot["createdAt"] = now.isoformat()
            for job in snapshot["jobs"].values():
                job["nextExpectedAt"] = (now + timedelta(hours=24)).isoformat()
        return value, [], 0


class MonitorTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name) / "monitor"
        self.collector = FakeCollector()
        self.messages = []
        self.configuration = {"schemaVersion": 1, "frontendEnabled": True,
                              "notificationsEnabled": True, "thresholds": {}}
        self.monitor = runtime.Monitor(self.root, self.collector, self.send)

    def send(self, incidents, keys, now):
        self.messages.append(list(keys))

    def test_oom_counter_survives_failed_collection_and_detects_next_increment(self):
        # Arrange
        cgroup = Mock()
        cgroup.read_text.side_effect = ["oom_kill 3\n", OSError("simulated failure"), "oom_kill 4\n", "oom_kill 0\n"]
        collector = Collector(root=Path(self.directory.name), cgroup=cgroup,
                              runner=lambda arguments: (1, ""),
                              probe=lambda service, path: (False, (NOW + timedelta(days=90)).isoformat()))
        monitor = runtime.Monitor(self.root, collector, self.send)
        configuration = self.configuration | {"notificationsEnabled": False}
        reports = []
        counters = []
        # Act
        for minute in range(4):
            reports.append(monitor.run(configuration, NOW + timedelta(minutes=minute)))
            counters.append(read_json(self.root / "state.json")["oomKills"])
        # Assert
        self.assertEqual([3, 3, 4, 0], counters)
        self.assertIn("host.oom", reports[1]["unknownChecks"])
        self.assertNotIn("host.oom", reports[1]["openIncidents"])
        self.assertIn("host.oom", reports[2]["openIncidents"])
        self.assertEqual([], self.messages)

    def test_run_reserves_delivery_before_post_and_recovers_after_three_samples(self):
        # Arrange
        self.collector.value["newHostOom"] = True
        def verify_reserved(incidents, keys, now):
            state = read_json(self.root / "state.json")
            self.assertEqual([now.isoformat()], state["deliveries"])
            self.assertEqual(1, state["incidents"]["host.oom"]["attempts"])
            self.assertEqual("unconfirmed", state["incidents"]["host.oom"]["delivery"])
            self.send(incidents, keys, now)
        self.monitor.notifier = verify_reserved
        # Act
        report = self.monitor.run(self.configuration, NOW)
        # Assert
        self.assertEqual("degraded", report["health"])
        self.assertEqual([["host.oom"]], self.messages)
        self.assertIsNone(report["notificationError"])
        self.monitor.notifier = self.send
        self.collector.value["newHostOom"] = False
        for minute in range(1, 4):
            report = self.monitor.run(self.configuration, NOW + timedelta(minutes=minute))
        self.assertEqual([], report["openIncidents"])
        self.assertEqual([["host.oom"], ["host.oom"]], self.messages)
        self.assertEqual("sent", read_json(self.root / "state.json")["incidents"]["host.oom"]["delivery"])

    def test_failed_notification_remains_visible_between_explicit_attempts(self):
        # Arrange
        self.collector.value["newHostOom"] = True
        def fail(incidents, keys, now):
            self.send(incidents, keys, now)
            raise RuntimeError("secret value must not be persisted")
        self.monitor.notifier = fail
        # Act / Assert
        for minute in (0, 1, 15, 16, 30, 31, 45):
            report = self.monitor.run(self.configuration, NOW + timedelta(minutes=minute))
            self.assertEqual("EMAIL_DELIVERY_UNCONFIRMED", report["notificationError"])
        self.assertEqual(3, len(self.messages))
        self.assertNotIn("secret", (self.root / "status.json").read_text())
        self.assertNotIn("secret", (self.root / "state.json").read_text())
        incident = read_json(self.root / "state.json")["incidents"]["host.oom"]
        self.assertFalse(incident["pending"])
        self.assertNotIn("lastNotifiedAt", incident)

    def test_disabled_notifications_observe_without_sending(self):
        # Arrange
        self.configuration["notificationsEnabled"] = False
        self.collector.value["newHostOom"] = True
        # Act
        report = self.monitor.run(self.configuration, NOW)
        # Assert
        self.assertFalse(report["notificationsEnabled"])
        self.assertEqual([], self.messages)
        self.assertEqual([], read_json(self.root / "state.json")["deliveries"])

    def test_concurrent_monitor_is_rejected_without_observation_or_send(self):
        # Arrange
        self.root.mkdir()
        with (self.root / "monitor.lock").open("w") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            # Act / Assert
            with self.assertRaises(BlockingIOError):
                self.monitor.run(self.configuration, NOW)
        self.assertFalse((self.root / "state.json").exists())
        self.assertEqual([], self.messages)

    def test_corrupt_state_is_not_reset_or_sent(self):
        # Arrange
        self.monitor.run(self.configuration, NOW)
        (self.root / "state.json").write_text("{}")
        # Act / Assert
        with self.assertRaises(ValueError):
            self.monitor.run(self.configuration, NOW)
        self.assertEqual("{}", (self.root / "state.json").read_text())
        self.assertEqual([], self.messages)

    def test_state_rejects_untyped_pending_or_unrecognized_delivery(self):
        # Arrange
        self.collector.value["newHostOom"] = True
        self.monitor.run(self.configuration, NOW)
        state = read_json(self.root / "state.json")
        # Act / Assert
        for field, value in (("pending", "true"), ("delivery", "secret")):
            invalid = copy.deepcopy(state)
            invalid["incidents"]["host.oom"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                runtime.validate_state(invalid, NOW)

    def test_configuration_requires_explicit_activation_flags(self):
        # Arrange / Act / Assert
        for value in ({}, self.configuration | {"frontendEnabled": 1},
                      self.configuration | {"notificationsEnabled": "true"},
                      self.configuration | {"schemaVersion": True}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                runtime.settings(value)

    def test_complete_observation_window_reports_healthy_and_validates_container_history(self):
        # Arrange
        self.root.mkdir()
        earlier = NOW - timedelta(minutes=10)
        append_history(self.root / "history", earlier, {"api": snapshot(when=earlier)})
        append_history(self.root / "history", NOW - timedelta(minutes=5),
                       {"api": snapshot(when=NOW - timedelta(minutes=5))})
        self.collector.value["snapshots"]["api"] = snapshot(count=20)
        previous = runtime.empty_state()
        previous["containerHistory"] = [{"service": "api", "id": "a" * 64, "oom": False,
                                         "restarts": 0, "createdAt": earlier.isoformat()}]
        atomic_json(self.root / "state.json", previous)
        # Act
        report = self.monitor.run(self.configuration, NOW)
        # Assert
        self.assertEqual("healthy", report["health"])
        self.assertEqual([], report["unknownChecks"])

    def test_notify_reads_only_private_credentials_at_delivery(self):
        # Arrange
        incident = {"host.oom": {"open": True}}
        with patch.object(runtime, "read_json", return_value={"fake": True}) as read, \
                patch.object(runtime.monitor_gmail, "send") as send:
            # Act
            runtime.notify(incident, ["host.oom"], NOW)
            # Assert
            read.assert_called_once_with(runtime.CREDENTIALS, private=True)
            send.assert_called_once_with({"fake": True}, incident, ["host.oom"], NOW)

    def test_old_terminal_counter_is_not_realerted_after_monitor_observation_gap(self):
        # Arrange
        operation = "AuthenticationEmailDelivery"
        self.collector.value["snapshots"]["worker"]["jobs"][operation]["terminalFailures"] = 1
        # Act
        for minute in (0, 1, 2, 3, 30):
            report = self.monitor.run(self.configuration, NOW + timedelta(minutes=minute))
        # Assert
        self.assertNotIn("worker." + operation + ".terminal", report["openIncidents"])
        self.assertEqual(2, len(self.messages))
