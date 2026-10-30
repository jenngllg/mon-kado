"""Pair-aware use of the native backend deployment and rollback engine."""

import copy
import hashlib

import frontend_contract
import frontend_runtime as hosting
from deploy_engine import Engine
from deploy_policy import decision, ensure
from release_catalog import identifiers


class CoordinatedBackend:
    """Retain the native engine's checks and restore only the approved previous release."""

    def __init__(self, store, runtime, frontend, gate, clock, approved):
        self.store = store
        self.runtime = runtime
        self.frontend = frontend
        self.gate = gate
        self.clock = clock
        self.approved = approved

    def prepare(self, previous, candidate):
        """Check active bytes, protected candidate and native preflight before maintenance."""
        state = self.store.load()
        ensure(state["phase"] in {"succeeded", "acknowledged"} and state["current"] == previous["backend"],
               "BACKEND_CHANGED")
        ensure(self.approved() == candidate["backend"], "BACKEND_APPROVAL_CHANGED")
        self.runtime.verify_active(previous["backend"])
        policy = decision(previous["backend"], candidate["backend"], self.runtime.history())
        ensure(not policy["migrate"] and policy["rollback"], "PAIRED_ROLLBACK_UNAVAILABLE")
        self.runtime.preflight(candidate["backend"], previous["backend"])

    def install(self, previous, candidate):
        """Apply the approved native release using the normal durable backend engine."""
        self.gate.verify()
        state = self.store.load()
        ensure(state["phase"] in {"succeeded", "acknowledged"} and state["current"] == previous["backend"],
               "BACKEND_CHANGED")
        ensure(self.approved() == candidate["backend"], "BACKEND_APPROVAL_CHANGED")
        installed = Engine(self.store, self.runtime, self.clock).deploy(candidate["backend"])
        ensure(installed["phase"] == "succeeded" and installed["current"] == candidate["backend"] and
               installed["checks"] == {"technical": True, "functional": True, "frontend": True},
               "BACKEND_VERIFICATION_FAILED")

    def _baseline_for_frontend(self, state, previous, candidate):
        """Bind backend smokes to the exact currently installed, retained frontend."""
        active = hosting.current_revision(self.frontend.publisher.releases)
        allowed = {previous["frontend"]["revision"]: previous, candidate["frontend"]["revision"]: candidate}
        ensure(active in allowed, "FRONTEND_CHANGED")
        frontend = allowed[active]["frontend"]
        self.frontend.verify_retained(allowed[active])
        baseline = copy.deepcopy(state["baseline"])
        ensure(isinstance(baseline, dict) and baseline.get("frontend") is not None, "FRONTEND_BASELINE_REQUIRED")
        baseline["frontend"] = frontend_contract.release_marker(active, frontend.get("googleEnabled", False))
        self.runtime.restore_baseline(baseline)

    def restore(self, previous, candidate):
        """Restore the prior native binaries without changing the database."""
        self.gate.verify()
        state = self.store.load()
        if state["phase"] in {"succeeded", "acknowledged", "rolledBack", "rejected"} and state["current"] == previous["backend"]:
            self.runtime.verify_active(previous["backend"])
            self.store.reconcile(state)
            return
        ensure(state["previous"] == previous["backend"] and state["candidate"] == candidate["backend"],
               "BACKEND_RECOVERY_MISMATCH")
        policy = decision(previous["backend"], candidate["backend"], self.runtime.history())
        ensure(not policy["migrate"] and policy["rollback"], "PAIRED_ROLLBACK_UNAVAILABLE")
        self._baseline_for_frontend(state, previous, candidate)
        state["rejectedPublication"] = candidate["backend"]["publicationId"]
        state["error"] = "OPERATOR_RECOVERY"
        Engine(self.store, self.runtime, self.clock).rollback(state)

    def verify(self, pair):
        """Verify the current native backend, SQL history and complete smoke checks."""
        self.gate.verify()
        state = self.store.load()
        ensure(state["current"] == pair["backend"] and state["phase"] in {"succeeded", "acknowledged", "rolledBack", "rejected"},
               "BACKEND_CHANGED")
        ensure(self.runtime.history() == identifiers(pair["backend"]["migrationCatalog"]), "DATABASE_HISTORY_DIVERGED")
        baseline = copy.deepcopy(state["baseline"])
        ensure(isinstance(baseline, dict) and baseline.get("frontend") is not None, "FRONTEND_BASELINE_REQUIRED")
        baseline["frontend"] = frontend_contract.release_marker(pair["frontend"]["revision"],
                                                                  pair["frontend"].get("googleEnabled", False))
        self.runtime.restore_baseline(baseline)
        self.runtime.verify_active(pair["backend"])
        checks = self.runtime.smoke(pair["backend"])
        ensure(checks == {"technical": True, "functional": True, "frontend": True}, "BACKEND_VERIFICATION_FAILED")

    def verify_contract(self, candidate):
        """Match the actual local HTTPS API schema to the protected frontend proof."""
        self.gate.verify()
        status, _, body = self.runtime.clients().request("GET", "/openapi/v1.json")
        ensure(status == 200 and hashlib.sha256(body).hexdigest() == candidate["proof"]["openapiSha256"],
               "BACKEND_CONTRACT_CHANGED")
