"""Fail-closed evidence checks for a coordinated frontend/backend rollout."""

from datetime import datetime, timedelta, timezone
import re

import frontend_contract
import release_manifest
from deploy_policy import decision, ensure

PHASES = {"prepared", "closingGate", "installingBackend", "capturingFrontendBackup", "installingFrontend",
          "verifyingCandidate", "committed", "recovering"}


def validate_pairs(previous, candidate, configuration_hash, database_history):
    """Validate each immutable frontend against its own exact backend.

    The caller must also verify the protected publication pointers, the active
    installation and all immutable artifact bytes before any live change.
    A coordinated rollback is admitted only for an unchanged SQL catalog and
    an explicit backend rollback declaration; it never restores the database.
    """
    for pair, expected in ((previous, {"frontend", "backend"}),
                           (candidate, {"frontend", "backend", "proof"})):
        ensure(isinstance(pair, dict) and set(pair) == expected, "PAIR_INVALID")
        backend = release_manifest.validate(pair["backend"], configuration_hash)
        ensure(backend["schemaVersion"] == 3, "NATIVE_PUBLICATION_REQUIRED")
        frontend_contract.validate(pair["frontend"], configuration_hash, backend["revision"])
    proof = candidate["proof"]
    ensure(isinstance(proof, dict) and set(proof) == {"schemaVersion", "frontendRevision", "backendRevision",
                                                     "configurationHash", "openapiSha256"}, "PAIR_PROOF_INVALID")
    ensure(type(proof["schemaVersion"]) is int and proof["schemaVersion"] == 1 and
           proof["frontendRevision"] == candidate["frontend"]["revision"] and
           proof["backendRevision"] == candidate["backend"]["revision"] and
           proof["configurationHash"] == configuration_hash and
           isinstance(proof["openapiSha256"], str) and re.fullmatch(r"[0-9a-f]{64}", proof["openapiSha256"]) is not None,
           "PAIR_PROOF_INVALID")
    policy = decision(previous["backend"], candidate["backend"], database_history)
    ensure(not policy["migrate"] and policy["rollback"], "PAIRED_ROLLBACK_UNAVAILABLE")
    ensure(previous["backend"]["revision"] != candidate["backend"]["revision"] and
           previous["frontend"]["revision"] != candidate["frontend"]["revision"], "PAIR_UNCHANGED")


def validate_intent(intent, configuration_hash, database_history, tool_digest):
    """Reject malformed recovery state without accepting a new or stale backup."""
    ensure(isinstance(intent, dict) and set(intent) == {"version", "phase", "previous", "candidate", "backup", "frontendBackup", "toolDigest"},
           "TRANSITION_INTENT_INVALID")
    ensure(type(intent["version"]) is int and intent["version"] == 1 and
           isinstance(intent["phase"], str) and intent["phase"] in PHASES and
           isinstance(intent["backup"], str) and re.fullmatch(r"[0-9a-f]{64}", intent["backup"]) is not None,
           "TRANSITION_INTENT_INVALID")
    ensure(isinstance(tool_digest, str) and re.fullmatch(r"[0-9a-f]{64}", tool_digest) is not None and
           intent["toolDigest"] == tool_digest, "TRANSITION_TOOL_CHANGED")
    ensure(intent["frontendBackup"] is None or isinstance(intent["frontendBackup"], str) and
           re.fullmatch(r"[0-9a-f]{64}", intent["frontendBackup"]) is not None and
           intent["frontendBackup"] != intent["backup"], "TRANSITION_INTENT_INVALID")
    ensure(intent["phase"] not in {"installingFrontend", "verifyingCandidate", "committed"} or
           intent["frontendBackup"] is not None, "TRANSITION_INTENT_INVALID")
    validate_pairs(intent["previous"], intent["candidate"], configuration_hash, database_history)


def validate_backup(state, snapshot, now, started):
    """Require this operation's fresh coherent capture and checked remote copy."""
    ensure(isinstance(snapshot, str) and re.fullmatch(r"[0-9a-f]{64}", snapshot) is not None,
           "BACKUP_REFERENCE_REQUIRED")
    ensure(isinstance(state, dict) and state.get("snapshotId") == snapshot and state.get("error") is None,
           "BACKUP_NOT_VERIFIED")
    ensure(isinstance(now, datetime) and now.tzinfo is not None and
           isinstance(started, datetime) and started.tzinfo is not None, "BACKUP_TIME_INVALID")
    try:
        capture, remote, integrity = (datetime.fromisoformat(state[key]) for key in
                                      ("lastCapture", "lastRemoteSuccess", "lastIntegrityCheck"))
        ensure(all(value.tzinfo is not None for value in (capture, remote, integrity)), "BACKUP_TIME_INVALID")
        now = now.astimezone(timezone.utc)
        started = started.astimezone(timezone.utc)
        capture = capture.astimezone(timezone.utc)
        remote = remote.astimezone(timezone.utc)
        integrity = integrity.astimezone(timezone.utc)
    except (KeyError, TypeError, ValueError, OverflowError):
        ensure(False, "BACKUP_TIME_INVALID")
    ensure(state.get("lastRemoteCapture") == state["lastCapture"] and
           started <= capture <= remote <= now and capture <= integrity <= now and
           now - capture <= timedelta(hours=1), "BACKUP_NOT_FRESH")
