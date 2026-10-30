"""Private durable intent for an interrupted coordinated rollout."""

import json
import os
from pathlib import Path

from deploy_policy import ensure
from deploy_storage import atomic_write, private_read, sync_directory

MAXIMUM_INTENT_BYTES = 131072


class CoordinatedStore:
    """Persist one fsynced transition in an existing private state directory."""

    def __init__(self, directory):
        self.directory = Path(directory)
        self.path = self.directory / "coordinated-transition.json"

    def load(self):
        """Read only a bounded, root-owned regular intent; refuse a dangling link."""
        ensure(not self.path.is_symlink(), "UNSAFE_TRANSITION_INTENT")
        if not self.path.exists():
            return None
        try:
            return json.loads(private_read(self.path, MAXIMUM_INTENT_BYTES))
        except (ValueError, UnicodeDecodeError):
            ensure(False, "TRANSITION_INTENT_INVALID")

    def save(self, value):
        """Fsync one complete intent before the next externally visible effect."""
        ensure(not self.path.is_symlink(), "UNSAFE_TRANSITION_INTENT")
        data = json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")
        ensure(len(data) <= MAXIMUM_INTENT_BYTES, "TRANSITION_INTENT_TOO_LARGE")
        atomic_write(self.path, data)

    def clear(self):
        """Remove intent only after the verified pair is serving and fsync the directory."""
        ensure(not self.path.is_symlink(), "UNSAFE_TRANSITION_INTENT")
        if not self.path.exists():
            return
        private_read(self.path, MAXIMUM_INTENT_BYTES)
        os.unlink(self.path)
        sync_directory(self.directory)
