"""Root-only command interface with bounded, non-sensitive diagnostics."""

import json
import os
import sys

from .operations import DEPLOYMENT, STATE, Operations, lock
from .policy import BackupError
from .restore import Restore
from .credentials import install_password


def execute(action, operations):
    """Dispatch explicit operations; never initialize a repository during backup."""
    if action == "status":
        print(json.dumps(operations.status(), sort_keys=True))
        return
    if action == "recover":
        if (STATE / "maintenance.json").exists():
            with lock(DEPLOYMENT / "backup-coordination.lock"), lock(DEPLOYMENT / "deploy.lock"):
                operations.recover()
        return
    if action == "credentials":
        install_password()
        return
    with lock(STATE / "backup.lock"):
        if action == "backup":
            operations.capture()
            operations.transfer()
        elif action == "transfer":
            operations.transfer()
        elif action == "resume":
            if operations.resume_capture() is False:
                print(json.dumps({"event": "backup_transfer_skipped", "code": "NO_PENDING_CAPTURE"}))
        elif action == "check":
            operations.check()
        elif action == "initialize":
            operations.restic("init")
        else:
            raise BackupError("UNKNOWN_OPERATION")


def main():
    """Do not expose exception messages, provider stderr, paths or credentials."""
    os.umask(0o077)
    operations = Operations()
    try:
        if os.geteuid() != 0:
            raise BackupError("ROOT_AND_OPERATION_REQUIRED")
        if len(sys.argv) == 4 and sys.argv[1] == "download":
            Restore(operations).download(sys.argv[2], sys.argv[3])
        elif len(sys.argv) == 4 and sys.argv[1] == "restore":
            print(json.dumps(Restore(operations).volumes(sys.argv[2], sys.argv[3])))
        elif len(sys.argv) == 2:
            execute(sys.argv[1], operations)
        else:
            raise BackupError("INVALID_ARGUMENTS")
        return 0
    except Exception as error:
        code = error.code if isinstance(error, BackupError) else "OPERATION_FAILED"
        try:
            operations.update(error=code)
        except Exception:
            pass
        print(json.dumps({"event": "backup_operation_failed", "code": code}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
