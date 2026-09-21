"""Deterministic monitoring policy tests; no network, clock waits or production state."""

from datetime import datetime, timedelta, timezone
import copy
import unittest

import monitor_policy as policy

NOW = datetime(2026, 9, 21, 12, tzinfo=timezone.utc)


def snapshot(service="api", when=NOW, count=0):
    return {"schemaVersion": 1, "service": service, "bootId": "a" * 32, "createdAt": when.isoformat(),
            "http": {"requests": count, "serverErrors": 0, "throttled": 0, "abandoned": 0,
                     "durationBuckets": [count, 0, 0, 0, 0, 0, 0, 0, 0]},
            "jobs": {name: {"state": "waiting", "nextExpectedAt": when.isoformat(), "consecutiveFailures": 0}
                     for name in policy.OPERATIONS} if service == "worker" else {}}


def observation():
    return {"frontendEnabled": True, "checks": {"api": False, "database": False, "frontend": False},
            "maintenance": [], "containers": {name: {"running": True, "newOom": False, "recentRestarts": 0}
                                               for name in policy.SERVICES}, "newHostOom": False,
            "snapshots": {"api": snapshot(), "worker": snapshot("worker")},
            "backup": {"lastRemoteCapture": NOW.isoformat(), "lastIntegrityCheck": NOW.isoformat(),
                       "terminalFailure": False, "missedScheduledCapture": False},
            "certificates": {"api": (NOW + timedelta(days=90)).isoformat()}}


class PolicyTests(unittest.TestCase):
    def setUp(self):
        self.options = policy.configuration({})

    def test_configuration_and_timestamps_reject_unsafe_values(self):
        # Arrange / Act / Assert
        self.assertEqual(policy.DEFAULTS, self.options)
        for value in ([], {"unknown": 1}, {"failureSamples": True}, {"failureSamples": 0},
                      {"httpErrorPercent": 101}, {"hourlyEmails": 61}, {"maximumAttempts": 11},
                      {"retrySeconds": 1}, {"maintenanceSeconds": 901}, {"hourlyEmails": 7},
                      {"maximumAttempts": 4}, {"retrySeconds": 899}, {"reminderSeconds": 86399},
                      {"restartWindowSeconds": 601}, {"httpWindowSeconds": 59}, {"latencyWindowSeconds": 601}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                policy.configuration(value)
        for value in (None, "bad", "2026-09-21T12:00:00"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                policy.timestamp(value)
        self.assertEqual(NOW, policy.timestamp("2026-09-21T14:00:00+02:00"))
        with self.assertRaises(ValueError):
            policy.age((NOW + timedelta(seconds=1)).isoformat(), NOW)
        for value, expected in ((0, True), (0.1, True), (-1, False), (True, False), (float("inf"), False), ("1", False)):
            self.assertEqual(expected, policy.finite_number(value))

    def test_snapshot_validates_all_shapes_and_operation_names(self):
        # Arrange
        value = snapshot("worker")
        value["jobs"][policy.OPERATIONS[0]] = {"state": "waiting", "consecutiveFailures": 0, "nextExpectedAt": NOW.isoformat()}
        # Act / Assert
        self.assertEqual(value, policy.validate_snapshot(value, "worker"))
        invalid = [None, {}, dict(value, schemaVersion=2), dict(value, service="other"), dict(value, bootId="z" * 32),
                   dict(value, http={}), dict(value, jobs={"private-email": {}})]
        for change in ({"durationBuckets": [0]}, {"durationBuckets": [-1] * 9}, {"requests": True}):
            invalid.append(dict(value, http=value["http"] | change))
        for job in ({}, {"state": "waiting", "consecutiveFailures": 0}, {"state": "running", "consecutiveFailures": 0},
                    {"state": "disabled", "consecutiveFailures": -1}):
            invalid.append(dict(value, jobs={policy.OPERATIONS[0]: job}))
        for item in invalid:
            with self.subTest(item=item), self.assertRaises(ValueError):
                policy.validate_snapshot(item, "worker")

    def test_observations_preserve_unknown_and_maintenance_has_a_deadline(self):
        # Arrange
        value = observation()
        value["maintenance"] = [(NOW - timedelta(minutes=15)).isoformat()]
        value["containers"]["worker"] = None
        value["backup"] = None
        value["certificates"]["api"] = None
        # Act
        result = policy.evaluate(value, [], NOW, self.options)
        # Assert
        self.assertIsNone(result["api.unavailable"])
        self.assertIsNone(result["worker.stopped"])
        self.assertTrue(result["backup.unavailable"])
        self.assertIsNone(result["api.certificate"])
        self.assertFalse(result["maintenance.overdue"])
        result = policy.evaluate(value, [], NOW + timedelta(seconds=1), self.options)
        self.assertTrue(result["maintenance.overdue"])
        self.assertFalse(result["api.unavailable"])

    def test_frontend_transition_suppresses_only_frontend_availability_for_fifteen_minutes(self):
        # Arrange
        value = observation()
        value["frontendMaintenance"] = [(NOW - timedelta(minutes=15)).isoformat()]
        value["checks"] = {"api": True, "database": True, "frontend": True}
        value["newHostOom"] = True
        value["backup"]["terminalFailure"] = True
        # Act
        result = policy.evaluate(value, [], NOW, self.options)
        # Assert
        self.assertIsNone(result["frontend.unavailable"])
        self.assertTrue(result["api.unavailable"])
        self.assertTrue(result["database.unavailable"])
        self.assertTrue(result["host.oom"])
        self.assertTrue(result["backup.failed"])
        result = policy.evaluate(value, [], NOW + timedelta(seconds=1), self.options)
        self.assertTrue(result["frontend.unavailable"])
        self.assertTrue(result["maintenance.overdue"])

    def test_stale_backups_jobs_and_certificates_trigger_without_sensitive_values(self):
        # Arrange
        value = observation()
        value["frontendEnabled"] = False
        value["snapshots"]["api"] = None
        value["backup"].update(lastRemoteCapture=None, lastIntegrityCheck=None, terminalFailure=True, missedScheduledCapture=True)
        value["certificates"]["api"] = (NOW + timedelta(days=13)).isoformat()
        for name, job in zip(policy.OPERATIONS, (
                {"state": "waiting", "consecutiveFailures": 3, "nextExpectedAt": NOW.isoformat()},
                {"state": "running", "consecutiveFailures": 0, "startedAt": (NOW - timedelta(minutes=16)).isoformat()},
                {"state": "waiting", "consecutiveFailures": 0, "nextExpectedAt": (NOW - timedelta(minutes=3)).isoformat()},
                {"state": "disabled", "consecutiveFailures": 9})):
            value["snapshots"]["worker"]["jobs"][name] = job
        # Act
        result = policy.evaluate(value, [], NOW, self.options)
        # Assert
        self.assertNotIn("frontend.unavailable", result)
        for key in ("api.telemetry", "backup.stale", "backup.integrity", "backup.failed", "backup.missed", "api.certificate"):
            self.assertTrue(result[key])
        for name in policy.OPERATIONS[:3]:
            self.assertTrue(result["worker." + name])
        self.assertFalse(result["worker.PersonalDataExport"])

    def test_http_requires_a_full_window_and_enough_requests(self):
        # Arrange
        old = snapshot(when=NOW - timedelta(minutes=10))
        recent = snapshot(when=NOW - timedelta(minutes=5))
        current = snapshot(count=20)
        current["http"]["serverErrors"] = 5
        current["http"]["durationBuckets"] = [0, 0, 0, 0, 0, 0, 20, 0, 0]
        # Act / Assert
        result = policy.http_decisions(current, [old, recent], NOW, self.options, False)
        self.assertEqual({"http.errors": True, "http.latency": True}, result)
        for previous in ([], [dict(old, bootId="b" * 32)]):
            self.assertEqual({"http.errors": None, "http.latency": None}, policy.http_decisions(current, previous, NOW, self.options, False))
        self.assertEqual({"http.errors": None, "http.latency": None}, policy.http_decisions(current, [], NOW, self.options, True))
        self.assertIsNone(policy.http_decisions(snapshot(count=1), [old], NOW, self.options, False)["http.latency"])
        corrupted = copy.deepcopy(old)
        corrupted["http"]["requests"] = 100
        with self.assertRaises(ValueError):
            policy.counter_window([corrupted], current, NOW, 600)

    def test_incident_debounce_recovery_reminder_and_unknown_do_not_flap(self):
        # Arrange
        state = {}
        # Act / Assert
        for index in range(3):
            state = policy.transition(state, {"api.unavailable": True}, NOW + timedelta(minutes=index), self.options)
            self.assertEqual(index == 2, state["api.unavailable"]["open"])
        opened = copy.deepcopy(state)
        state = policy.transition(state, {"api.unavailable": None}, NOW + timedelta(minutes=3), self.options)
        opened["api.unavailable"].update(badSamples=0, goodSamples=0)
        self.assertEqual(opened, state)
        policy.delivery_result(state, ["api.unavailable"], NOW + timedelta(minutes=3), self.options, True)
        state = policy.transition(state, {}, NOW + timedelta(minutes=4), self.options)
        self.assertFalse(state["api.unavailable"]["pending"])
        state = policy.transition(state, {}, NOW + timedelta(days=1, minutes=4), self.options)
        self.assertTrue(state["api.unavailable"]["pending"])
        for index in range(3):
            state = policy.transition(state, {"api.unavailable": False}, NOW + timedelta(days=1, minutes=5 + index), self.options)
        self.assertFalse(state["api.unavailable"]["open"])
        self.assertEqual(2, state["api.unavailable"]["generation"])
        self.assertTrue(policy.transition({}, {"host.oom": True}, NOW, self.options)["host.oom"]["open"])
        self.assertFalse(policy.transition({}, {"api.unavailable": None}, NOW, self.options)["api.unavailable"]["open"])

    def test_notification_rate_limit_explicit_retries_and_failed_delivery_are_durable(self):
        # Arrange
        state = policy.transition({}, {"host.oom": True}, NOW, self.options)
        # Act / Assert
        self.assertEqual(["host.oom"], policy.notifications(state, [], NOW, self.options)[0])
        self.assertEqual([], policy.notifications(state, [NOW.isoformat()] * 6, NOW, self.options)[0])
        for attempt in range(3):
            now = NOW + timedelta(minutes=15 * attempt)
            policy.delivery_result(state, ["host.oom"], now, self.options, False)
            self.assertEqual([], policy.notifications(state, [], now, self.options)[0])
        self.assertFalse(state["host.oom"]["pending"])
        self.assertNotIn("lastNotifiedAt", state["host.oom"])
        state = policy.transition(state, {"host.oom": True}, NOW + timedelta(days=2), self.options)
        self.assertTrue(state["host.oom"]["pending"])
        policy.delivery_result(state, ["host.oom"], NOW + timedelta(days=2), self.options, True)
        self.assertEqual("sent", state["host.oom"]["delivery"])

    def test_unknown_sample_interrupts_consecutive_failures_and_recovery(self):
        # Arrange
        state = {}
        # Act / Assert
        for decision in (True, True, None, True):
            state = policy.transition(state, {"api.unavailable": decision}, NOW, self.options)
        self.assertFalse(state["api.unavailable"]["open"])
        for decision in (True, True, False, False, None, False):
            state = policy.transition(state, {"api.unavailable": decision}, NOW, self.options)
        self.assertTrue(state["api.unavailable"]["open"])
        self.assertEqual(1, state["api.unavailable"]["goodSamples"])

    def test_optional_job_measurements_are_whitelisted_and_typed(self):
        # Arrange
        value = snapshot("worker")
        job = {"state": "disabled", "consecutiveFailures": 0, "successes": 0, "failures": 0,
               "terminalFailures": 0, "lastDurationMilliseconds": 1.5, "lastCompletedAt": NOW.isoformat()}
        value["jobs"][policy.OPERATIONS[0]] = job
        # Act / Assert
        self.assertEqual(value, policy.validate_snapshot(value, "worker"))
        for field, bad in (("successes", True), ("failures", -1), ("terminalFailures", "secret"),
                           ("lastDurationMilliseconds", float("nan")), ("lastCompletedAt", "secret")):
            invalid = copy.deepcopy(value)
            invalid["jobs"][policy.OPERATIONS[0]][field] = bad
            with self.subTest(field=field), self.assertRaises(ValueError):
                policy.validate_snapshot(invalid, "worker")

    def test_worker_terminal_failure_is_a_new_event_and_not_repeated_cumulative_count(self):
        # Arrange
        observed = observation()
        worker = observed["snapshots"]["worker"]
        name = policy.OPERATIONS[0]
        worker["jobs"][name]["terminalFailures"] = 1
        # Act / Assert
        self.assertTrue(policy.evaluate(observed, [], NOW, self.options)["worker." + name + ".terminal"])
        self.assertFalse(policy.evaluate(observed, [copy.deepcopy(worker)], NOW, self.options)["worker." + name + ".terminal"])
        empty = snapshot("worker")
        empty["jobs"] = {}
        with self.assertRaises(ValueError):
            policy.validate_snapshot(empty, "worker")
