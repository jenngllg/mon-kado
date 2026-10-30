"""Restore the traffic gate before Caddy starts after a host restart."""

import json
import os
from pathlib import Path
import sys

from coordinated_gate import Gate
from deploy_policy import DeploymentError, ensure

STATE = Path("/var/lib/monkado-deployment")


def guard(state, gate):
    """Keep a recorded or pre-existing maintenance gate closed at boot."""
    ensure(state.is_dir() and not state.is_symlink(), "TRANSITION_STATE_UNAVAILABLE")
    intent = state / "coordinated-transition.json"
    if os.path.lexists(intent):
        # Any entry, including a damaged file or symlink, is uncertainty and
        # must close the gate. Only explicit recovery can remove the intent.
        gate.close()
        return "closed"
    if gate.exists():
        # An orphaned gate can be evidence of an interrupted effect. Never
        # remove it automatically merely because the journal is missing.
        gate.verify()
        return "closed"
    return "open"


def main():
    """Return a fixed diagnostic and fail the dependent Caddy unit on error."""
    try:
        ensure(os.geteuid() == 0 and len(sys.argv) == 1, "ROOT_REQUIRED")
        print(json.dumps({"gate": guard(STATE, Gate())}))
        return 0
    except (DeploymentError, OSError, ValueError, TypeError, KeyError):
        print(json.dumps({"error": "TRANSITION_BOOT_GUARD_FAILED"}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
