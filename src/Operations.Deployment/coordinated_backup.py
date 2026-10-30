"""Create and verify a fresh offsite snapshot for a coordinated rollout."""

from datetime import datetime, timezone
import json
import subprocess

from coordinated_policy import validate_backup
from deploy_policy import DeploymentError, ensure

BACKUP = "/usr/local/sbin/monkado-backup"


def run(action, inherited=False):
    """Run one fixed backup operation without exposing credentials or provider output."""
    ensure(action in {"backup", "check", "status"}, "BACKUP_OPERATION_INVALID")
    environment = {"PATH": "/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin", "HOME": "/root",
                   "LANG": "C.UTF-8", "TZ": "UTC"}
    if inherited:
        ensure(action == "backup", "BACKUP_OPERATION_INVALID")
        environment.update(MONKADO_BACKUP_COORDINATION_FD="8", MONKADO_BACKUP_DEPLOYMENT_FD="9")
    try:
        result = subprocess.run([BACKUP, action], env=environment, pass_fds=(8, 9) if inherited else (),
                                capture_output=True, timeout=3600, check=False)
    except (OSError, subprocess.TimeoutExpired, ValueError):
        raise DeploymentError("COORDINATED_BACKUP_FAILED") from None
    ensure(result.returncode == 0 and len(result.stdout) <= 16384, "COORDINATED_BACKUP_FAILED")
    return result.stdout


def capture(run_operation=run, clock=lambda: datetime.now(timezone.utc), gate=None, inherited=False):
    """Return only a freshly captured, remotely copied and fully checked snapshot ID.

    When the gate is closed, the caller holds the coordination and deployment
    locks on descriptors 8 and 9 throughout capture, transfer and verification.
    """
    started = clock()
    if gate is not None:
        gate.verify()
    run_operation("backup", inherited=inherited)
    run_operation("check", inherited=False)
    try:
        state = json.loads(run_operation("status", inherited=False))
        snapshot = state["snapshotId"]
    except (ValueError, KeyError, TypeError):
        raise DeploymentError("COORDINATED_BACKUP_FAILED") from None
    validate_backup(state, snapshot, clock(), started)
    if gate is not None:
        gate.verify()
    return snapshot
