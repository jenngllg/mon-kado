"""Validate durable evidence for the coordination tool's own installation."""

import re
from datetime import datetime, timedelta

from coordinated_integrity import BACKUP_MODULES, validate
from deploy_policy import ensure
import frontend_contract
import release_manifest

PHASES = {"prepared", "staging", "guarding", "swappingBackup", "capturing", "verifying", "committed"}
HASH = r"[0-9a-f]{64}"


def validate_install_intent(intent, configuration):
    """Reject missing evidence, changed inventories and malformed recovery state."""
    ensure(isinstance(intent, dict) and set(intent) == {"version", "phase", "manifest", "backup",
                                                       "previousBackupFiles", "previousPair", "backupStarted", "postInstallBackup"},
           "TOOL_INSTALL_INTENT_INVALID")
    ensure(type(intent["version"]) is int and intent["version"] == 1 and
           isinstance(intent["phase"], str) and intent["phase"] in PHASES and
           isinstance(intent["backup"], str) and re.fullmatch(HASH, intent["backup"]) is not None,
           "TOOL_INSTALL_INTENT_INVALID")
    validate(intent["manifest"], configuration)
    try:
        started = datetime.fromisoformat(intent["backupStarted"])
        ensure(started.utcoffset() == timedelta(0), "TOOL_INSTALL_INTENT_INVALID")
    except (TypeError, ValueError):
        ensure(False, "TOOL_INSTALL_INTENT_INVALID")
    pair = intent["previousPair"]
    ensure(isinstance(pair, dict) and set(pair) == {"frontend", "backend"}, "TOOL_INSTALL_INTENT_INVALID")
    backend = release_manifest.validate(pair["backend"], configuration)
    ensure(backend["schemaVersion"] == 3, "NATIVE_PUBLICATION_REQUIRED")
    frontend_contract.validate(pair["frontend"], configuration, backend["revision"])
    old = intent["previousBackupFiles"]
    ensure(isinstance(old, dict) and set(old) == set(BACKUP_MODULES) and
           all(isinstance(value, str) and re.fullmatch(HASH, value) is not None for value in old.values()),
           "TOOL_INSTALL_INTENT_INVALID")
    after = intent["postInstallBackup"]
    ensure(after is None or isinstance(after, str) and re.fullmatch(HASH, after) is not None and after != intent["backup"],
           "TOOL_INSTALL_INTENT_INVALID")
    ensure(intent["phase"] not in {"verifying", "committed"} or after is not None, "TOOL_INSTALL_INTENT_INVALID")
