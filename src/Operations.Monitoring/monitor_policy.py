"""Deterministic alert decisions; measurements and incident state never contain member data."""

from datetime import datetime, timedelta, timezone
import math

UTC = timezone.utc
SERVICES = ("api", "worker", "postgres", "caddy")
OPERATIONS = (
    "AuthenticationEmailDelivery", "WishlistModerationEmailDelivery", "AccountErasureProcessing",
    "PersonalDataExport", "UnconfirmedAccountCleanup", "ExpiredAuthenticationSessionCleanup",
    "ExpiredMemberEmailChangeRequestCleanup", "ProcessedAuthenticationEmailCleanup",
    "ExpiredGuestSessionCleanup", "GiftImageCleanup",
)
DEFAULTS = {
    "failureSamples": 3, "recoverySamples": 3, "snapshotSeconds": 180,
    "maintenanceSeconds": 900, "workerLateSeconds": 120, "workerRunningSeconds": 900,
    "workerFailures": 3, "restartCount": 3, "restartWindowSeconds": 600,
    "httpWindowSeconds": 300, "httpMinimumErrors": 5, "httpErrorPercent": 20,
    "latencyWindowSeconds": 600, "latencyMinimumRequests": 20, "latencyMilliseconds": 2000,
    "backupHours": 30, "integrityHours": 192, "certificateDays": 14,
    "reminderSeconds": 86400, "hourlyEmails": 6, "retrySeconds": 900, "maximumAttempts": 3,
}


def require(condition):
    """Reject malformed state without echoing any input value."""
    if not condition:
        raise ValueError("INVALID_MONITORING_DATA")


def configuration(value):
    """Use explicit bounded defaults; reject unknown options and non-finite numbers."""
    require(isinstance(value, dict) and set(value) <= set(DEFAULTS))
    result = DEFAULTS | value
    require(all(type(number) is int and 1 <= number <= 604800 for number in result.values()))
    require(result["httpErrorPercent"] <= 100 and result["hourlyEmails"] <= 6)
    require(result["maximumAttempts"] <= 3 and result["retrySeconds"] >= 900)
    require(result["reminderSeconds"] >= 86400)
    # The fixed twelve-minute reader and private state cap must accommodate every allowed window.
    require(all(60 <= result[key] <= 600 for key in ("restartWindowSeconds", "httpWindowSeconds", "latencyWindowSeconds")))
    require(result["maintenanceSeconds"] <= 900)
    return result


def timestamp(value):
    """Require timezone-aware timestamps and normalize to UTC."""
    require(isinstance(value, str) and len(value) <= 40)
    parsed = datetime.fromisoformat(value)
    require(parsed.tzinfo is not None)
    return parsed.astimezone(UTC)


def age(value, now):
    """Reject future timestamps rather than extending freshness after clock errors."""
    seconds = (now - timestamp(value)).total_seconds()
    require(seconds >= 0)
    return seconds


def finite_number(value):
    """Accept nonnegative finite JSON measurements, never booleans or arbitrary objects."""
    return type(value) in (int, float) and math.isfinite(value) and value >= 0


def validate_snapshot(value, service):
    """Check the versioned private application snapshot before consuming counters."""
    require(isinstance(value, dict) and set(value) == {"schemaVersion", "service", "bootId", "createdAt", "http", "jobs"})
    require(type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1)
    require(value.get("service") == service and service in ("api", "worker"))
    require(isinstance(value.get("bootId"), str) and len(value["bootId"]) == 32)
    require(all(character in "0123456789abcdef" for character in value["bootId"]))
    timestamp(value.get("createdAt"))
    http = value.get("http")
    require(isinstance(http, dict) and set(http) == {"requests", "serverErrors", "throttled", "abandoned", "durationBuckets"})
    require(all(type(http.get(key)) is int and http[key] >= 0 for key in ("requests", "serverErrors", "throttled", "abandoned")))
    buckets = http.get("durationBuckets")
    require(isinstance(buckets, list) and len(buckets) == 9)
    require(all(type(count) is int and count >= 0 for count in buckets))
    jobs = value.get("jobs")
    require(isinstance(jobs, dict) and set(jobs) <= set(OPERATIONS))
    require(set(jobs) == (set(OPERATIONS) if service == "worker" else set()))
    for job in jobs.values():
        validate_job(job)
    return value


def validate_job(job):
    """Validate one worker operation independently of the enclosing snapshot."""
    require(isinstance(job, dict) and job.get("state") in ("running", "waiting", "disabled"))
    require(set(job) <= {"state", "consecutiveFailures", "startedAt", "nextExpectedAt", "lastCompletedAt",
                        "successes", "failures", "terminalFailures", "lastDurationMilliseconds"})
    require(type(job.get("consecutiveFailures")) is int and job["consecutiveFailures"] >= 0)
    for key in ("successes", "failures", "terminalFailures"):
        if key in job:
            require(type(job[key]) is int and job[key] >= 0)
    if "lastDurationMilliseconds" in job:
        require(finite_number(job["lastDurationMilliseconds"]))
    for key in ("startedAt", "nextExpectedAt", "lastCompletedAt"):
        if job.get(key) is not None:
            timestamp(job[key])
    require(job["state"] != "running" or job.get("startedAt") is not None)
    require(job["state"] != "waiting" or job.get("nextExpectedAt") is not None)


def counter_window(history, current, now, seconds):
    """Calculate deltas only within one process lifetime and a fully observed time window."""
    candidates = [entry for entry in history if entry["service"] == current["service"] and entry["bootId"] == current["bootId"]
                  and seconds <= age(entry["createdAt"], now) <= seconds + 120]
    if not candidates:
        return None
    baseline = max(candidates, key=lambda entry: timestamp(entry["createdAt"]))
    delta = {key: current["http"][key] - baseline["http"][key]
             for key in ("requests", "serverErrors", "throttled", "abandoned")}
    delta["durationBuckets"] = [new - old for new, old in zip(current["http"]["durationBuckets"], baseline["http"]["durationBuckets"])]
    require(all(value >= 0 for key, value in delta.items() if key != "durationBuckets"))
    require(all(value >= 0 for value in delta["durationBuckets"]))
    return delta


def maintenance(observation, now, options):
    """Only explicit, recent maintenance markers suppress expected availability failures."""
    markers = observation.get("maintenance", [])
    require(isinstance(markers, list) and len(markers) <= 2)
    if not markers:
        return False, False
    oldest = max(age(marker, now) for marker in markers)
    return oldest <= options["maintenanceSeconds"], oldest > options["maintenanceSeconds"]


def evaluate(observation, history, now, options):
    """Return stable incident keys with bad/healthy/unknown states, never raw exceptions."""
    suppressed, overdue = maintenance(observation, now, options)
    frontend_suppressed, frontend_overdue = maintenance({"maintenance": observation.get("frontendMaintenance", [])}, now, options)
    decisions = {"maintenance.overdue": overdue or frontend_overdue}
    decisions.update(availability_decisions(observation, suppressed, frontend_suppressed))
    decisions.update(container_decisions(observation, options, suppressed))
    decisions.update(snapshot_decisions(observation, history, now, options, suppressed))
    decisions.update(http_decisions(observation["snapshots"].get("api"), history, now, options, suppressed))
    decisions.update(backup_decisions(observation.get("backup"), now, options))
    for host, expiry in observation["certificates"].items():
        require(host in ("api", "frontend"))
        decisions[host + ".certificate"] = None if expiry is None else timestamp(expiry) - now < timedelta(days=options["certificateDays"])
    return decisions


def availability_decisions(observation, suppressed, frontend_suppressed):
    """Apply frontend maintenance only to its own availability probe."""
    decisions = {}
    for name in ("api", "database", "frontend"):
        if name != "frontend" or observation["frontendEnabled"]:
            decisions[name + ".unavailable"] = None if suppressed or name == "frontend" and frontend_suppressed else observation["checks"].get(name)
    return decisions


def container_decisions(observation, options, suppressed):
    """Never suppress resource incidents during an application maintenance window."""
    decisions = {}
    for name in SERVICES:
        container = observation["containers"].get(name)
        decisions[name + ".stopped"] = None if suppressed or container is None else not container["running"]
        decisions[name + ".oom"] = None if container is None else container["newOom"]
        decisions[name + ".restarts"] = None if container is None else container["recentRestarts"] >= options["restartCount"]
    decisions["host.oom"] = observation.get("newHostOom")
    decisions["monitor.collection"] = any(value is None for value in observation["containers"].values()) or observation.get("newHostOom") is None
    return decisions


def snapshot_decisions(observation, history, now, options, suppressed):
    """Separate freshness from the result of actual worker cycles."""
    decisions = {}
    for service in ("api", "worker"):
        snapshot = observation["snapshots"].get(service)
        fresh = snapshot is not None and age(snapshot["createdAt"], now) <= options["snapshotSeconds"]
        decisions[service + ".telemetry"] = None if suppressed else not fresh
        if service == "worker" and fresh:
            decisions.update(worker_decisions(snapshot, history, now, options, suppressed))
    return decisions


def worker_decisions(snapshot, history, now, options, suppressed):
    """Compare terminal failures only with the same worker process lifetime."""
    previous = [entry for entry in history if entry["service"] == "worker" and entry["bootId"] == snapshot["bootId"]]
    baseline = max(previous, key=lambda entry: timestamp(entry["createdAt"]), default=None)
    decisions = {}
    for name, job in snapshot["jobs"].items():
        bad = job_failed(job, now, options)
        decisions["worker." + name] = None if suppressed else bad
        previous_failures = baseline["jobs"][name].get("terminalFailures", 0) if baseline is not None else 0
        decisions["worker." + name + ".terminal"] = job.get("terminalFailures", 0) > previous_failures
    return decisions


def job_failed(job, now, options):
    """Evaluate a cycle's failure streak or overdue progress without conflating disabled jobs."""
    bad = job["consecutiveFailures"] >= options["workerFailures"]
    if job["state"] == "running":
        bad = bad or age(job["startedAt"], now) > options["workerRunningSeconds"]
    if job["state"] == "waiting":
        bad = bad or now > timestamp(job["nextExpectedAt"]) + timedelta(seconds=options["workerLateSeconds"])
    return bad and job["state"] != "disabled"


def backup_decisions(backup, now, options):
    """Use capture age, not transfer age, to assess the retained recovery point."""
    decisions = {}
    decisions["backup.unavailable"] = backup is None
    if backup is not None:
        captured = backup.get("lastRemoteCapture")
        checked = backup.get("lastIntegrityCheck")
        decisions["backup.stale"] = captured is None or age(captured, now) > options["backupHours"] * 3600
        decisions["backup.integrity"] = checked is None or age(checked, now) > options["integrityHours"] * 3600
        decisions["backup.failed"] = bool(backup.get("terminalFailure"))
        decisions["backup.missed"] = bool(backup.get("missedScheduledCapture"))
    return decisions


def http_decisions(snapshot, history, now, options, suppressed):
    """Do not infer successful service from missing data, restarts or insufficient traffic."""
    result = {"http.errors": None, "http.latency": None}
    if suppressed or snapshot is None or age(snapshot["createdAt"], now) > options["snapshotSeconds"]:
        return result
    errors = counter_window(history, snapshot, now, options["httpWindowSeconds"])
    latency = counter_window(history, snapshot, now, options["latencyWindowSeconds"])
    if errors is not None:
        result["http.errors"] = (errors["serverErrors"] >= options["httpMinimumErrors"] and
                                 errors["serverErrors"] * 100 >= options["httpErrorPercent"] * errors["requests"])
    if latency is not None:
        buckets = latency["durationBuckets"]
        total = sum(buckets)
        if total >= options["latencyMinimumRequests"]:
            bounds = (50, 100, 250, 500, 1000, 2000, 5000, 10000, math.inf)
            fast = sum(count for count, bound in zip(buckets, bounds) if bound <= options["latencyMilliseconds"])
            result["http.latency"] = fast * 100 < total * 95
    return result


def transition(previous, decisions, now, options):
    """Debounce durable incidents and preserve open incidents while evidence is unavailable."""
    result = {}
    for key in sorted(set(previous) | set(decisions)):
        result[key] = transition_incident(key, previous.get(key), decisions.get(key), now, options)
    return result


def transition_incident(key, previous, decision, now, options):
    """Apply one decision without changing unrelated incident generations."""
    incident = dict(previous or {"open": False, "badSamples": 0, "goodSamples": 0, "generation": 0})
    require(decision is None or type(decision) is bool)
    update_samples(incident, decision, options)
    immediate = not (key.endswith(".stopped") or key in {"api.unavailable", "database.unavailable", "frontend.unavailable"})
    opening = decision is True and not incident["open"] and (immediate or incident["badSamples"] >= options["failureSamples"])
    closing = decision is False and incident["open"] and incident["goodSamples"] >= options["recoverySamples"]
    if opening or closing:
        incident.update(open=opening, changedAt=now.isoformat(), generation=incident["generation"] + 1,
                        pending=True, attempts=0, nextAttemptAt=now.isoformat())
    else:
        schedule_reminder(incident, now, options)
    return incident


def update_samples(incident, decision, options):
    """Missing evidence interrupts consecutive samples but cannot close an incident."""
    if decision is None:
        incident.update(badSamples=0, goodSamples=0)
        return
    incident["badSamples"] = min(options["failureSamples"], incident["badSamples"] + 1) if decision else 0
    incident["goodSamples"] = 0 if decision else min(options["recoverySamples"], incident["goodSamples"] + 1)


def schedule_reminder(incident, now, options):
    """Only an open incident without an outstanding attempt can receive a reminder."""
    if incident["open"] and not incident.get("pending", False):
        last = incident.get("lastAttemptAt", incident["changedAt"])
        if age(last, now) >= options["reminderSeconds"]:
            incident.update(pending=True, attempts=0, nextAttemptAt=now.isoformat())


def notifications(incidents, sent, now, options):
    """Batch all due transitions into a single bounded email, preserving a durable hourly cap."""
    recent = [item for item in sent if age(item, now) < 3600]
    if len(recent) >= options["hourlyEmails"]:
        return [], recent
    due = [key for key, incident in sorted(incidents.items()) if incident.get("pending") and
           timestamp(incident["nextAttemptAt"]) <= now and incident["attempts"] < options["maximumAttempts"]]
    return due, recent


def delivery_result(incidents, keys, now, options, successful):
    """Record explicit attempts; uncertain delivery is not success and may later duplicate mail."""
    for key in keys:
        incident = incidents[key]
        incident["attempts"] += 1
        incident["lastAttemptAt"] = now.isoformat()
        incident["nextAttemptAt"] = (now + timedelta(seconds=options["retrySeconds"])).isoformat()
        if successful or incident["attempts"] >= options["maximumAttempts"]:
            incident.update(pending=False)
        if successful:
            incident["lastNotifiedAt"] = now.isoformat()
        incident["delivery"] = "sent" if successful else "unconfirmed"
