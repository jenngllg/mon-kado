"""Pure deployment decisions; a database rollback is never an available operation."""

import re
from release_catalog import fingerprint, identifiers, require, validate_catalog

PHASES = {"idle", "rejected", "prepared", "stopping", "migrating", "starting", "verifying", "rollingBack",
          "verifyingRollback", "succeeded", "rolledBack", "recoveryRequired", "acknowledged"}
TERMINAL = {"idle", "rejected", "succeeded", "rolledBack", "acknowledged"}


class DeploymentError(Exception):
    """Carry only a fixed safe diagnostic across the operational boundary."""

    def __init__(self, code):
        super().__init__(code)
        self.code = code


def ensure(condition, code):
    """Raise an operational failure without disclosing input or subprocess output."""
    if not condition:
        raise DeploymentError(code)


def decision(current, candidate, history):
    """Require exact current history and append-only migrations before maintenance."""
    require(candidate["schemaVersion"] == 2, "ADOPTION_REQUIRED")
    catalog = validate_catalog(candidate["migrationCatalog"])
    require(fingerprint(catalog) == candidate["migrationHash"])
    expected = identifiers(catalog)
    if current is None:
        ensure(not history, "ADOPTION_REQUIRED")
        return {"migrate": True, "rollback": False}
    previous = current["migrationCatalog"]
    ensure(history == identifiers(previous), "DATABASE_HISTORY_DIVERGED")
    old_entries = previous["migrations"]
    ensure(catalog["migrations"][:len(old_entries)] == old_entries, "MIGRATION_HISTORY_REWRITTEN")
    unchanged = previous == catalog
    ensure(unchanged or len(catalog["migrations"]) > len(old_entries), "MODEL_CHANGED_WITHOUT_MIGRATION")
    ensure(current["configurationHash"] == candidate["configurationHash"], "CONFIGURATION_BASELINE_REQUIRED")
    return {"migrate": not unchanged, "rollback": unchanged and candidate["rollbackAllowed"]}


def initial_state():
    """Initialize an installation without claiming a trusted previous release."""
    return {"schemaVersion": 1, "phase": "idle", "current": None, "previous": None,
            "candidate": None, "rejectedPublication": None, "error": None,
            "startedAt": None, "finishedAt": None, "checks": {}, "baseline": None}


def validate_state(value):
    """Reject incomplete or unknown state rather than treating it as an initial deployment."""
    require(isinstance(value, dict) and set(value) == set(initial_state()), "INVALID_DEPLOYMENT_STATE")
    require(type(value["schemaVersion"]) is int and value["schemaVersion"] == 1, "INVALID_DEPLOYMENT_STATE")
    require(isinstance(value["phase"], str) and value["phase"] in PHASES, "INVALID_DEPLOYMENT_STATE")
    require(isinstance(value["checks"], dict), "INVALID_DEPLOYMENT_STATE")
    require(set(value["checks"]).issubset({"technical", "functional", "frontend"}) and
            all(type(item) is bool for item in value["checks"].values()), "INVALID_DEPLOYMENT_STATE")
    for name, pattern in (("error", r"[A-Z][A-Z0-9_]{0,79}"), ("rejectedPublication", r"[0-9]{1,20}-[0-9]{1,10}"),
                          ("startedAt", r"[0-9T:+.Z-]{1,40}"), ("finishedAt", r"[0-9T:+.Z-]{1,40}")):
        require(value[name] is None or isinstance(value[name], str) and re.fullmatch(pattern, value[name]) is not None,
                "INVALID_DEPLOYMENT_STATE")
    require(value["baseline"] is None or isinstance(value["baseline"], dict), "INVALID_DEPLOYMENT_STATE")
    return value


def dotenv(release):
    """Keep the three-line compatibility contract consumed by backup tooling."""
    return (f'API_IMAGE={release["apiImage"]}\nWORKER_IMAGE={release["workerImage"]}\n'
            f'RELEASE_REVISION={release["revision"]}\n')
