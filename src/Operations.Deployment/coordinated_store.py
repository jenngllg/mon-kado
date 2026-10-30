"""Private durable intent for an interrupted coordinated rollout."""

import json
import os
from pathlib import Path
import re

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

    def complete(self, intent):
        """Persist a deterministic non-secret receipt before removing the intent."""
        publication = intent["candidate"]["backend"]["publicationId"]
        ensure(isinstance(publication, str) and re.fullmatch(r"[1-9][0-9]{0,19}-[1-9][0-9]{0,5}", publication),
               "TRANSITION_INTENT_INVALID")
        receipt = {"schemaVersion": 1, "publicationId": publication,
                   "previousBackend": intent["previous"]["backend"]["revision"],
                   "previousFrontend": intent["previous"]["frontend"]["revision"],
                   "backend": intent["candidate"]["backend"]["revision"],
                   "frontend": intent["candidate"]["frontend"]["revision"],
                   "backendSnapshot": intent["backup"], "frontendSnapshot": intent["frontendBackup"],
                   "toolDigest": intent["toolDigest"]}
        data = json.dumps(receipt, sort_keys=True, separators=(",", ":")).encode("utf-8")
        target = self.directory / ("coordinated-receipt-" + publication + ".json")
        ensure(not target.is_symlink(), "UNSAFE_TRANSITION_RECEIPT")
        if target.exists():
            ensure(private_read(target, MAXIMUM_INTENT_BYTES) == data, "TRANSITION_RECEIPT_CHANGED")
        else:
            atomic_write(target, data)


class InstallationStore(CoordinatedStore):
    """Retain independent tool-install intent and its non-secret snapshot receipt."""

    def __init__(self, directory):
        super().__init__(directory)
        self.path = self.directory / "coordinated-install.json"

    def complete(self, intent):
        """Record both immutable backup references before clearing install intent."""
        revision = intent["manifest"]["revision"]
        ensure(isinstance(revision, str) and re.fullmatch(r"[0-9a-f]{40}", revision), "TOOL_INSTALL_INTENT_INVALID")
        receipt = {"schemaVersion": 1, "revision": revision, "preInstallSnapshot": intent["backup"],
                   "postInstallSnapshot": intent["postInstallBackup"]}
        data = json.dumps(receipt, sort_keys=True, separators=(",", ":")).encode("utf-8")
        target = self.directory / ("coordinated-install-receipt-" + revision + ".json")
        ensure(not target.is_symlink(), "UNSAFE_TRANSITION_RECEIPT")
        if target.exists():
            ensure(private_read(target, MAXIMUM_INTENT_BYTES) == data, "TRANSITION_RECEIPT_CHANGED")
        else:
            atomic_write(target, data)
