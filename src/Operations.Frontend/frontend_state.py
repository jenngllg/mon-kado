"""Versioned local release evidence; legacy metadata never implies a fresh health check."""

from datetime import datetime, timezone
import hashlib
import json

import frontend_contract as contract

PHASES = {"idle", "legacy", "healthy", "preparing", "activating", "recovering", "rolledBack",
          "recoveryRequired", "failed", "unavailable"}
ERRORS = {None, "PUBLICATION_FAILED", "ROLLOUT_INTERRUPTED", "ROLLBACK_FAILED", "NO_PREVIOUS_RELEASE"}
FIELDS = {"schemaVersion", "phase", "revision", "lastVerifiedRevision", "candidateRevision", "error",
          "paused", "resumeDigest", "updatedAt", "checks"}


def now():
    """Use UTC only for human-readable operational evidence, never timeout control."""
    return datetime.now(timezone.utc).isoformat()


def initial(revision=None):
    """An existing unverified installation requires fresh checks before it can be called healthy."""
    return {"schemaVersion": 2, "phase": "legacy" if revision else "idle", "revision": revision,
            "lastVerifiedRevision": None, "candidateRevision": None, "error": None, "paused": False,
            "resumeDigest": None, "updatedAt": None, "checks": {}}


def validate(value):
    """Reject unknown local fields and unbounded/untrusted diagnostic text."""
    contract.require(isinstance(value, dict) and set(value) == FIELDS)
    contract.require(type(value["schemaVersion"]) is int and value["schemaVersion"] == 2)
    contract.require(value["phase"] in PHASES and value["error"] in ERRORS)
    contract.require(type(value["paused"]) is bool)
    for field in ("revision", "lastVerifiedRevision", "candidateRevision"):
        contract.require(value[field] is None or contract.matches(contract.SHA, value[field]))
    contract.require(value["resumeDigest"] is None or contract.matches(contract.DIGEST, value["resumeDigest"]))
    timestamp = value["updatedAt"]
    if timestamp is not None:
        contract.require(isinstance(timestamp, str) and len(timestamp) <= 40)
        contract.require(datetime.fromisoformat(timestamp).utcoffset() == timezone.utc.utcoffset(None))
    checks = value["checks"]
    contract.require(isinstance(checks, dict) and set(checks) <= {"files", "https"})
    contract.require(all(type(item) is bool for item in checks.values()))
    return value


def read(path, revision=None):
    """Read bounded regular metadata and conservatively adopt the historical status format."""
    if not path.exists():
        contract.require(not path.is_symlink())
        return initial(revision)
    contract.require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 16384)
    value = json.loads(path.read_text(encoding="utf-8"))
    contract.require(isinstance(value, dict))
    if "schemaVersion" in value:
        return validate(value)
    contract.require(value.get("state") in {"healthy", "failed"})
    result = initial(revision)
    if value["state"] == "failed":
        result.update(phase="failed", paused=True, error="PUBLICATION_FAILED")
    return result


def resume_digest(manifest):
    """Bind one operator authorization to the complete manifest, not only its source commit."""
    return hashlib.sha256(json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
