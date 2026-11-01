"""Private, durable deployment state and compatibility markers on the local filesystem."""

import json
import os
import stat
import tempfile
from pathlib import Path

from deploy_policy import dotenv, ensure, initial_state, validate_state


def private_read(path, maximum=131072):
    """Read bounded regular root-owned files without following a final symlink."""
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, "rb") as stream:
        metadata = os.fstat(stream.fileno())
        ensure(stat.S_ISREG(metadata.st_mode) and metadata.st_uid == 0 and metadata.st_gid == 0 and
               stat.S_IMODE(metadata.st_mode) == 0o600, "UNSAFE_PRIVATE_FILE")
        value = stream.read(maximum + 1)
    ensure(len(value) <= maximum, "PRIVATE_FILE_TOO_LARGE")
    return value


def sync_directory(directory):
    """Make renamed state durable across sudden power loss."""
    descriptor = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def atomic_write(path, value):
    """Replace one root-only file after fsync; never truncate the existing state in place."""
    descriptor, temporary = tempfile.mkstemp(prefix=".deployment-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(value)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        sync_directory(path.parent)
    finally:
        Path(temporary).unlink(missing_ok=True)


class Store:
    """Persist the authoritative JSON before publishing derived legacy references."""

    def __init__(self, directory):
        self.directory = directory

    def load(self):
        """Require explicit adoption of an existing v1 installation."""
        target = self.directory / "status.json"
        if not target.exists():
            ensure(not target.is_symlink(), "UNSAFE_PRIVATE_FILE")
            ensure(not (self.directory / "current.env").exists(), "ADOPTION_REQUIRED")
            return initial_state()
        return validate_state(json.loads(private_read(target)))

    def save(self, value):
        """Atomically retain a complete state transition."""
        atomic_write(self.directory / "status.json", json.dumps(validate_state(value), sort_keys=True).encode())

    def begin(self, value):
        """Persist intent before a compatibility marker and before stopping anything."""
        self.save(value)
        atomic_write(self.directory / "in-progress.env", dotenv(value["candidate"]).encode())

    def finish(self, value):
        """Commit the authoritative result before removing the maintenance marker."""
        self.save(value)
        self.reconcile(value)

    def reconcile(self, value):
        """Repair derived files only from a terminal, durably committed state."""
        ensure(value["phase"] in {"succeeded", "rejected", "rolledBack", "acknowledged"}, "RECOVERY_REQUIRED")
        ensure(value["current"] is not None, "RECOVERY_REQUIRED")
        atomic_write(self.directory / "current.env", dotenv(value["current"]).encode())
        (self.directory / "in-progress.env").unlink(missing_ok=True)
        sync_directory(self.directory)
