"""Fixed-command host entry point; never echo arguments, settings or provider diagnostics."""

from datetime import datetime, timezone
import json
import os
import sys

import monitor_policy as policy
import monitor_runtime as runtime
from monitor_storage import atomic_json, read_json

FAILURE_FILE = "failure.json"


def status(root, now):
    """Report only the bounded local status contract and detect a stopped monitor."""
    value = read_json(root / "status.json")
    policy.require(isinstance(value, dict) and set(value) == {
        "schemaVersion", "createdAt", "health", "openIncidents", "unknownChecks", "notificationError", "notificationsEnabled"})
    policy.require(type(value["schemaVersion"]) is int and value["schemaVersion"] == 1)
    policy.require(value["health"] in ("healthy", "unknown", "degraded"))
    policy.require(type(value["notificationsEnabled"]) is bool)
    policy.require(value["notificationError"] in (None, "EMAIL_DELIVERY_UNCONFIRMED"))
    for name in ("openIncidents", "unknownChecks"):
        policy.require(isinstance(value[name], list) and len(value[name]) <= 100)
        policy.require(all(isinstance(key, str) and len(key) <= 100 and
                           all(character.isascii() and (character.isalnum() or character == ".") for character in key)
                           for key in value[name]))
    stale = policy.age(value["createdAt"], now) > 180
    failed = (root / FAILURE_FILE).exists()
    return value | {"health": "degraded" if stale or failed else value["health"],
                    "monitorStale": stale, "monitorFailed": failed}


def execute(arguments, now, root=runtime.STATE, configuration_path=runtime.SETTINGS, monitor=None):
    """Execute only documented read-only checks or an explicitly requested observation cycle."""
    policy.require(arguments in (["run"], ["status"], ["check-config"], ["test-email"]))
    policy.require(os.geteuid() == 0)
    if arguments == ["status"]:
        return status(root, now)
    configuration = runtime.settings(read_json(configuration_path))
    if arguments == ["check-config"]:
        return {"configurationValid": True, "notificationsEnabled": configuration["notificationsEnabled"],
                "frontendEnabled": configuration["frontendEnabled"]}
    observer = monitor if monitor is not None else runtime.Monitor(root)
    if arguments == ["test-email"]:
        return observer.run(configuration, now, test_email=True)
    result = observer.run(configuration, now)
    (root / FAILURE_FILE).unlink(missing_ok=True)
    return result


def main(arguments=None):
    """Keep all failure output fixed; preserve evidence of a failed monitor invocation."""
    arguments = sys.argv[1:] if arguments is None else arguments
    now = datetime.now(timezone.utc)
    try:
        result = execute(arguments, now)
    except Exception:
        if arguments == ["run"] and os.geteuid() == 0:
            try:
                atomic_json(runtime.STATE / FAILURE_FILE, {"createdAt": now.isoformat(), "error": "MONITOR_OPERATION_FAILED"})
            except Exception:
                pass
        print(json.dumps({"error": "MONITOR_OPERATION_FAILED"}))
        return 1
    print(json.dumps(result, sort_keys=True))
    return 1 if result.get("emailAcknowledged") is False else 0


if __name__ == "__main__":
    raise SystemExit(main())
