"""Read only MK-820's bounded operational outcome; never trigger deployment or rollback."""

from monitor_policy import age, require
from monitor_storage import read_json

PHASES = {"idle", "rejected", "prepared", "stopping", "migrating", "starting", "verifying", "rollingBack",
          "verifyingRollback", "succeeded", "rolledBack", "recoveryRequired", "acknowledged"}
ACTIVE = {"prepared", "stopping", "migrating", "starting", "verifying", "rollingBack", "verifyingRollback"}


def collect(root, runner, now):
    """Return fixed booleans only, including failures before the maintenance marker exists."""
    state = read_json(root / "monkado-deployment/status.json", private=True)
    require(isinstance(state, dict) and type(state.get("schemaVersion")) is int and state["schemaVersion"] == 1)
    phase = state.get("phase")
    require(isinstance(phase, str) and phase in PHASES)
    error = state.get("error")
    require(error is None or isinstance(error, str))
    code, raw = runner(["systemctl", "show", "monkado-deploy.service", "-p", "ActiveState", "-p", "Result"])
    require(code == 0)
    fields = {key: value for key, value in (line.split("=", 1) for line in raw.splitlines())}
    require(set(fields) == {"ActiveState", "Result"})
    require(fields["ActiveState"] in {"active", "activating", "deactivating", "inactive", "failed"})
    active = fields["ActiveState"] in {"active", "activating", "deactivating"}
    overdue = False
    if phase in ACTIVE:
        overdue = age(state.get("startedAt"), now) > 900
    return {"failed": bool(error) or not active and fields["Result"] != "success",
            "recoveryRequired": phase == "recoveryRequired" or phase in ACTIVE and (not active or overdue),
            "rollbackFailed": error == "ROLLBACK_FAILED"}


def decisions(observed):
    """Unknown evidence cannot clear an existing deployment incident."""
    return {"deployment.unavailable": observed is None,
            "deployment.failed": None if observed is None else observed["failed"],
            "deployment.recoveryRequired": None if observed is None else observed["recoveryRequired"],
            "deployment.rollbackFailed": None if observed is None else observed["rollbackFailed"]}
