"""Read only MK-820's bounded operational outcome; never trigger deployment or rollback."""

from monitor_policy import age, require
from monitor_storage import read_json

PHASES = {"idle", "rejected", "prepared", "stopping", "migrating", "starting", "verifying", "rollingBack",
          "verifyingRollback", "succeeded", "rolledBack", "recoveryRequired", "acknowledged"}
ACTIVE = {"prepared", "stopping", "migrating", "starting", "verifying", "rollingBack", "verifyingRollback"}


def service_state(runner, service):
    """Read a fixed unit's result without exposing command output in public observations."""
    code, raw = runner(["systemctl", "show", service, "-p", "ActiveState", "-p", "Result"])
    require(code == 0)
    fields = {key: value for key, value in (line.split("=", 1) for line in raw.splitlines())}
    require(set(fields) == {"ActiveState", "Result"})
    require(fields["ActiveState"] in {"active", "activating", "deactivating", "inactive", "failed"})
    active = fields["ActiveState"] in {"active", "activating", "deactivating"}
    return active, fields["Result"]


def collect(root, runner, now):
    """Return fixed booleans only, including failures before the maintenance marker exists."""
    state = read_json(root / "monkado-deployment/status.json", private=True)
    require(isinstance(state, dict) and type(state.get("schemaVersion")) is int and state["schemaVersion"] == 1)
    phase = state.get("phase")
    require(isinstance(phase, str) and phase in PHASES)
    error = state.get("error")
    require(error is None or isinstance(error, str))
    active, result = service_state(runner, "monkado-deploy.service")
    overdue = False
    if phase in ACTIVE:
        overdue = age(state.get("startedAt"), now) > 900
    return {"failed": bool(error) or not active and result != "success",
            "recoveryRequired": phase == "recoveryRequired" or phase in ACTIVE and (not active or overdue),
            "rollbackFailed": error == "ROLLBACK_FAILED"}


def decisions(observed):
    """Unknown evidence cannot clear an existing deployment incident."""
    return {"deployment.unavailable": observed is None,
            "deployment.failed": None if observed is None else observed["failed"],
            "deployment.recoveryRequired": None if observed is None else observed["recoveryRequired"],
            "deployment.rollbackFailed": None if observed is None else observed["rollbackFailed"]}


def collect_frontend(root, runner, now):
    """Observe frontend release failures even when a restored site is responding normally."""
    state = read_json(root / "monkado-frontend/status.json", private=True)
    require(isinstance(state, dict) and type(state.get("schemaVersion")) is int and state["schemaVersion"] == 2)
    phase = state.get("phase")
    require(isinstance(phase, str) and phase in {"idle", "legacy", "healthy", "preparing", "activating", "recovering",
                                              "rolledBack", "recoveryRequired", "failed", "unavailable"})
    require(phase not in {"idle", "legacy"})
    require(type(state.get("paused")) is bool)
    error = state.get("error")
    require(error is None or error in {"PUBLICATION_FAILED", "ROLLOUT_INTERRUPTED", "ROLLBACK_FAILED", "NO_PREVIOUS_RELEASE"})
    running, result = service_state(runner, "monkado-frontend.service")
    interrupted = phase in {"preparing", "activating", "recovering"} and (not running or age(state.get("updatedAt"), now) > 600)
    return {"failed": error is not None or not running and result != "success",
            "recoveryRequired": phase == "recoveryRequired" or interrupted,
            "rollbackFailed": error == "ROLLBACK_FAILED"}


def frontend_decisions(observed):
    """A deliberate pause alone is not a failure and a successful rollback does not clear one."""
    return {"frontend." + key: value for key, value in decisions(observed).items()}
