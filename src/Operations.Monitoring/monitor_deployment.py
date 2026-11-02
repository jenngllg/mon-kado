"""Read only MK-820's bounded operational outcome; never trigger deployment or rollback."""

import os
from pathlib import Path
import re
import stat

from monitor_policy import age, require
from monitor_storage import read_json

PHASES = {"idle", "rejected", "prepared", "stopping", "migrating", "starting", "verifying", "rollingBack",
          "verifyingRollback", "succeeded", "rolledBack", "recoveryRequired", "acknowledged"}
ACTIVE = {"prepared", "stopping", "migrating", "starting", "verifying", "rollingBack", "verifyingRollback"}


def active_lock(path, table=Path("/proc/locks")):
    """Observe the kernel's existing flock without acquiring or creating a deployment lock."""
    try:
        descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    except FileNotFoundError:
        return False
    with os.fdopen(descriptor, "rb") as source:
        info = os.fstat(source.fileno())
        require(stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_gid == 0 and stat.S_IMODE(info.st_mode) == 0o600)
        with table.open("r", encoding="ascii") as locks:
            data = locks.read(131073)
        require(len(data) <= 131072)
        for line in data.splitlines():
            match = re.fullmatch(r"\d+: FLOCK\s+ADVISORY\s+WRITE\s+-?\d+\s+([0-9a-f]+):([0-9a-f]+):(\d+)\s+0\s+EOF", line)
            if match is not None and (int(match[1], 16), int(match[2], 16), int(match[3])) == (
                    os.major(info.st_dev), os.minor(info.st_dev), info.st_ino):
                return True
    return False


def service_state(runner, service):
    """Read a fixed unit's result without exposing command output in public observations."""
    code, raw = runner(["systemctl", "show", service, "-p", "ActiveState", "-p", "Result"])
    require(code == 0)
    fields = {key: value for key, value in (line.split("=", 1) for line in raw.splitlines())}
    require(set(fields) == {"ActiveState", "Result"})
    require(fields["ActiveState"] in {"active", "activating", "deactivating", "inactive", "failed"})
    active = fields["ActiveState"] in {"active", "activating", "deactivating"}
    return active, fields["Result"]


def backend_state(root):
    """Validate backend operational evidence without reading release secrets."""
    state = read_json(root / "monkado-deployment/status.json", private=True)
    require(isinstance(state, dict) and type(state.get("schemaVersion")) is int and state["schemaVersion"] == 1)
    phase = state.get("phase")
    require(isinstance(phase, str) and phase in PHASES)
    error = state.get("error")
    require(error is None or isinstance(error, str))
    return state


def collect(root, runner, now, completed_at):
    """Return fixed booleans only, including failures before the maintenance marker exists."""
    state = backend_state(root)
    active, result = service_state(runner, "monkado-deploy.service")
    active = active or active_lock(root / "monkado-deployment/deploy.lock")
    # The publisher can finish between the first state read and the lock observation.
    if not active and state["phase"] in ACTIVE:
        state = backend_state(root)
    phase, error = state["phase"], state.get("error")
    now = completed_at(now)
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


def frontend_state(root):
    """Validate frontend publication state independently of the executor."""
    state = read_json(root / "monkado-frontend/status.json", private=True)
    require(isinstance(state, dict) and type(state.get("schemaVersion")) is int and state["schemaVersion"] == 2)
    phase = state.get("phase")
    require(isinstance(phase, str) and phase in {"idle", "legacy", "healthy", "preparing", "activating", "recovering",
                                              "rolledBack", "recoveryRequired", "failed", "unavailable"})
    require(phase not in {"idle", "legacy"})
    require(type(state.get("paused")) is bool)
    error = state.get("error")
    require(error is None or error in {"PUBLICATION_FAILED", "ROLLOUT_INTERRUPTED", "ROLLBACK_FAILED", "NO_PREVIOUS_RELEASE"})
    return state


def collect_frontend(root, runner, now, completed_at):
    """Observe frontend release failures even when a restored site is responding normally."""
    state = frontend_state(root)
    running, result = service_state(runner, "monkado-frontend.service")
    running = running or active_lock(root / "monkado-deployment/deploy.lock")
    if not running and state["phase"] in {"preparing", "activating", "recovering"}:
        state = frontend_state(root)
    phase, error = state["phase"], state.get("error")
    now = completed_at(now)
    interrupted = phase in {"preparing", "activating", "recovering"} and (not running or age(state.get("updatedAt"), now) > 600)
    return {"failed": error is not None or not running and result != "success",
            "recoveryRequired": phase == "recoveryRequired" or interrupted,
            "rollbackFailed": error == "ROLLBACK_FAILED"}


def frontend_decisions(observed):
    """A deliberate pause alone is not a failure and a successful rollback does not clear one."""
    return {"frontend." + key: value for key, value in decisions(observed).items()}
