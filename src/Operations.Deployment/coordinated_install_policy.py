"""Validate durable evidence for the coordination tool's own installation."""

import re
from datetime import datetime, timedelta

from coordinated_integrity import BACKUP_MODULES, CONFIG_UPGRADE_FILES, FILES, validate
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


def validate_upgrade_intent(intent):
    """Pin both authenticated inventories while permitting their configuration transition."""
    ensure(isinstance(intent, dict) and set(intent) == {"version", "phase", "manifest", "backup", "previousBackupFiles",
           "previousPair", "previousManifest", "previousFiles", "backupStarted", "postInstallBackup"}, "TOOL_UPGRADE_INTENT_INVALID")
    ensure(type(intent["version"]) is int and intent["version"] == 2 and intent["phase"] in PHASES,
           "TOOL_UPGRADE_INTENT_INVALID")
    old, new = intent["previousManifest"], intent["manifest"]
    validate(old, old["configurationHash"])
    validate(new, new["configurationHash"])
    ensure(set(new["files"]) == set(FILES) and old["revision"] != new["revision"], "TOOL_UPGRADE_INTENT_INVALID")
    pair = intent["previousPair"]
    ensure(isinstance(pair, dict) and set(pair) == {"backend", "frontend", "proof"}, "TOOL_UPGRADE_INTENT_INVALID")
    release_manifest.validate(pair["backend"], old["configurationHash"])
    frontend = pair["frontend"]
    frontend_contract.validate(frontend, frontend["configurationHash"], frontend["backendRevision"])
    proof = pair["proof"]
    ensure(isinstance(proof, dict) and set(proof) == {"schemaVersion", "frontendRevision", "backendRevision",
           "configurationHash", "openapiSha256"} and type(proof["schemaVersion"]) is int and proof["schemaVersion"] == 1 and
           proof["frontendRevision"] == frontend["revision"] and proof["backendRevision"] == frontend["backendRevision"] and
           proof["configurationHash"] == frontend["configurationHash"] and
           isinstance(proof["openapiSha256"], str) and re.fullmatch(HASH, proof["openapiSha256"]), "TOOL_UPGRADE_INTENT_INVALID")
    for name, expected in (("previousFiles", CONFIG_UPGRADE_FILES), ("previousBackupFiles", BACKUP_MODULES)):
        values = intent[name]
        ensure(isinstance(values, dict) and set(values) == set(expected) and
               all(isinstance(value, str) and re.fullmatch(HASH, value) for value in values.values()), "TOOL_UPGRADE_INTENT_INVALID")
    ensure(isinstance(intent["backup"], str) and re.fullmatch(HASH, intent["backup"]), "TOOL_UPGRADE_INTENT_INVALID")
    try:
        ensure(datetime.fromisoformat(intent["backupStarted"]).utcoffset() == timedelta(0), "TOOL_UPGRADE_INTENT_INVALID")
    except (ValueError, TypeError):
        ensure(False, "TOOL_UPGRADE_INTENT_INVALID")
    after = intent["postInstallBackup"]
    ensure(after is None or isinstance(after, str) and re.fullmatch(HASH, after) and after != intent["backup"], "TOOL_UPGRADE_INTENT_INVALID")
    ensure(intent["phase"] not in {"verifying", "committed"} or after is not None, "TOOL_UPGRADE_INTENT_INVALID")
