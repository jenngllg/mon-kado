"""Durable local incident orchestration. Observation failures never trigger automatic repairs."""

import fcntl
import os
from pathlib import Path
import re

from monitor_collect import Collector
import monitor_gmail
import monitor_policy as policy
from monitor_storage import append_history, atomic_json, read_json, recent_snapshots

STATE = Path("/var/lib/monkado-monitoring")
SETTINGS = Path("/etc/monkado/monitoring.json")
CREDENTIALS = Path("/etc/monkado/monitoring-gmail.json")


def notify(incidents, keys, now):
    """Read only the private Gmail copy when an explicitly enabled notification is due."""
    monitor_gmail.send(read_json(CREDENTIALS, private=True), incidents, keys, now)


def settings(value):
    """Require explicit activation flags and strongly bounded alert thresholds."""
    policy.require(isinstance(value, dict) and set(value) == {"schemaVersion", "frontendEnabled", "notificationsEnabled", "thresholds"})
    policy.require(type(value["schemaVersion"]) is int and value["schemaVersion"] == 1)
    policy.require(type(value["frontendEnabled"]) is bool and type(value["notificationsEnabled"]) is bool)
    return value | {"thresholds": policy.configuration(value["thresholds"])}


def empty_state():
    """Create local technical state without reading application storage."""
    return {"schemaVersion": 1, "incidents": {}, "deliveries": [], "containerHistory": [], "oomKills": None, "workerSnapshot": None}


def validate_state(value, now):
    """Reject corrupt durable state; never reset deduplication silently after an error."""
    policy.require(isinstance(value, dict) and set(value) == set(empty_state()))
    policy.require(value["schemaVersion"] == 1 and isinstance(value["incidents"], dict) and len(value["incidents"]) <= 100)
    for key, incident in value["incidents"].items():
        validate_incident(key, incident, now)
    policy.require(isinstance(value["deliveries"], list) and len(value["deliveries"]) <= 60)
    for delivered in value["deliveries"]:
        policy.age(delivered, now)
    policy.require(isinstance(value["containerHistory"], list) and len(value["containerHistory"]) <= 500)
    for item in value["containerHistory"]:
        policy.require(set(item) == {"service", "createdAt", "id", "oom", "restarts"})
        policy.require(item["service"] in policy.SERVICES and re.fullmatch(r"[0-9a-f]{64}", item["id"]) is not None)
        policy.require(type(item["oom"]) is bool and type(item["restarts"]) is int and item["restarts"] >= 0)
        policy.age(item["createdAt"], now)
    policy.require(value["oomKills"] is None or type(value["oomKills"]) is int and value["oomKills"] >= 0)
    if value["workerSnapshot"] is not None:
        policy.validate_snapshot(value["workerSnapshot"], "worker")
        policy.age(value["workerSnapshot"]["createdAt"], now)
    return value


def validate_incident(key, incident, now):
    """Keep persisted notification state strictly typed before any delivery is attempted."""
    policy.require(isinstance(key, str) and re.fullmatch(r"[A-Za-z][A-Za-z0-9.]{0,99}", key) is not None)
    policy.require(isinstance(incident, dict) and type(incident.get("open")) is bool)
    policy.require(type(incident.get("pending", False)) is bool)
    policy.require(incident.get("delivery") in (None, "sent", "unconfirmed"))
    policy.require(set(incident) <= {"open", "badSamples", "goodSamples", "generation", "changedAt", "pending", "attempts",
                                    "nextAttemptAt", "lastAttemptAt", "lastNotifiedAt", "delivery"})
    for name in ("badSamples", "goodSamples", "generation", "attempts"):
        policy.require(type(incident.get(name, 0)) is int and incident.get(name, 0) >= 0)
    for name in ("changedAt", "lastAttemptAt", "lastNotifiedAt"):
        if name in incident:
            policy.age(incident[name], now)
    policy.require(not incident["open"] or "changedAt" in incident)
    if incident.get("pending"):
        policy.timestamp(incident["nextAttemptAt"])


class Monitor:
    """Serialize observations and notification attempts under a nonblocking host-only lock."""

    def __init__(self, root=STATE, collector=None, notifier=notify):
        self.root = root
        self.collector = collector if collector is not None else Collector()
        self.notifier = notifier

    def run(self, configuration, now, test_email=False):
        """Reserve each delivery durably before its POST, bounding uncertain retries across crashes."""
        configuration = settings(configuration)
        options = configuration["thresholds"]
        self.root.mkdir(mode=0o700, exist_ok=True)
        policy.require(not self.root.is_symlink())
        descriptor = os.open(self.root / "monitor.lock", os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600)
        with os.fdopen(descriptor, "w") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            if test_email:
                return self.send_test(now, options)
            return self.observe(configuration, now, options)

    def send_test(self, now, options):
        """Send one explicitly requested test under the same durable hourly quota, never retrying."""
        state_path = self.root / "state.json"
        state = validate_state(read_json(state_path), now) if state_path.exists() else empty_state()
        recent = [item for item in state["deliveries"] if policy.age(item, now) < 3600]
        policy.require(len(recent) < options["hourlyEmails"])
        state["deliveries"] = recent + [now.isoformat()]
        atomic_json(state_path, state)
        result = {"createdAt": now.isoformat(), "emailAcknowledged": False, "error": "EMAIL_DELIVERY_UNCONFIRMED"}
        atomic_json(self.root / "test-email.json", result)
        try:
            self.notifier({"monitor.test": {"open": False, "changedAt": now.isoformat()}}, ["monitor.test"], now)
            result.update(emailAcknowledged=True, error=None)
        except Exception:
            pass
        atomic_json(self.root / "test-email.json", result)
        return result

    def observe(self, configuration, now, options):
        """Evaluate one sample; expose bounded status independently of notification availability."""
        state_path = self.root / "state.json"
        previous = validate_state(read_json(state_path), now) if state_path.exists() else empty_state()
        observed, samples, oom = self.collector.collect(now, previous, options, configuration["frontendEnabled"])
        history_path = self.root / "history"
        history = [policy.validate_snapshot(item, service) for service in ("api", "worker")
                   for item in recent_snapshots(history_path, now, service)]
        if previous["workerSnapshot"] is not None:
            history.append(previous["workerSnapshot"])
        decisions = policy.evaluate(observed, history, now, options)
        incidents = policy.transition(previous["incidents"], decisions, now, options)
        due, recent = policy.notifications(incidents, previous["deliveries"], now, options)
        container_history = [item for item in previous["containerHistory"] if policy.age(item["createdAt"], now) <= options["restartWindowSeconds"]]
        state = {"schemaVersion": 1, "incidents": incidents, "deliveries": recent,
                 "containerHistory": (container_history + samples)[-500:], "oomKills": oom,
                 "workerSnapshot": observed["snapshots"].get("worker") or previous["workerSnapshot"]}
        atomic_json(state_path, state)
        append_history(history_path, now, {"schemaVersion": 1, "createdAt": now.isoformat(),
                                          "api": observed["snapshots"].get("api"), "worker": observed["snapshots"].get("worker"),
                                          "checks": decisions})
        notification_error = None
        if configuration["notificationsEnabled"] and due:
            # Reserve an attempt before sending: a killed process cannot bypass the quota or retry budget.
            policy.delivery_result(incidents, due, now, options, False)
            state["deliveries"].append(now.isoformat())
            atomic_json(state_path, state)
            try:
                self.notifier(incidents, due, now)
                for key in due:
                    incidents[key].update(pending=False, delivery="sent", lastNotifiedAt=now.isoformat())
            except Exception:
                notification_error = "EMAIL_DELIVERY_UNCONFIRMED"
            atomic_json(state_path, state)
        if any(value.get("delivery") == "unconfirmed" for value in incidents.values()):
            notification_error = "EMAIL_DELIVERY_UNCONFIRMED"
        opened = sorted(key for key, value in incidents.items() if value["open"])
        unknown = sorted(key for key, value in decisions.items() if value is None)
        health = "healthy"
        if unknown:
            health = "unknown"
        if opened or notification_error:
            health = "degraded"
        report = {"schemaVersion": 1, "createdAt": now.isoformat(), "health": health, "openIncidents": opened,
                  "unknownChecks": unknown, "notificationError": notification_error,
                  "notificationsEnabled": configuration["notificationsEnabled"]}
        atomic_json(self.root / "status.json", report)
        return report
