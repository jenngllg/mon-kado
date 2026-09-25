"""Deployment transitions with injected effects and explicit recovery boundaries."""

from deploy_policy import DeploymentError, TERMINAL, decision, ensure
from release_catalog import identifiers


class Engine:
    """Apply one approved publication, retaining failed intent after a successful rollback."""

    def __init__(self, store, runtime, clock):
        self.store = store
        self.runtime = runtime
        self.clock = clock

    def transition(self, state, phase):
        """Persist the next possible side effect before executing it."""
        state["phase"] = phase
        self.store.save(state)

    def deploy(self, candidate):
        """Preflight first, then make at most one rollout and one admissible rollback."""
        state = self.store.load()
        ensure(state["phase"] in TERMINAL, "RECOVERY_REQUIRED")
        ensure(candidate["publicationId"] != state["rejectedPublication"], "PUBLICATION_REJECTED")
        if state["current"] is not None:
            self.runtime.verify_active(state["current"])
            self.store.reconcile(state)
            if candidate == state["current"]:
                return state
        try:
            baseline = self.runtime.preflight(candidate, state["current"])
            policy = decision(state["current"], candidate, self.runtime.history())
        except (DeploymentError, OSError, ValueError, KeyError, TypeError, AttributeError) as error:
            code = error.code if isinstance(error, DeploymentError) else "PREFLIGHT_FAILED"
            state.update(candidate=candidate, previous=state["current"], phase="rejected", error=code,
                         rejectedPublication=candidate["publicationId"], startedAt=self.clock(), finishedAt=self.clock())
            self.store.save(state)
            raise DeploymentError(code) from None
        state.update(candidate=candidate, previous=state["current"], phase="prepared", error=None,
                     startedAt=self.clock(), finishedAt=None, checks={}, baseline=baseline)
        self.store.begin(state)
        try:
            self.rollout(state, policy)
        except (DeploymentError, InterruptedError) as error:
            self.handle_failure(state, policy, error)
        return state

    def rollout(self, state, policy):
        """Never run migrations for the schema-unchanged rollback path."""
        self.transition(state, "stopping")
        self.runtime.stop()
        if policy["migrate"]:
            self.transition(state, "migrating")
            self.runtime.migrate(state["candidate"])
        self.transition(state, "starting")
        self.runtime.start(state["candidate"])
        self.transition(state, "verifying")
        state["checks"] = self.runtime.smoke(state["candidate"])
        ensure(self.runtime.history() == identifiers(state["candidate"]["migrationCatalog"]), "DATABASE_HISTORY_DIVERGED")
        state.update(current=state["candidate"], phase="succeeded", error=None,
                     rejectedPublication=None, finishedAt=self.clock())
        self.store.finish(state)

    def handle_failure(self, state, policy, error):
        """Keep uncertainty explicit; a failed rollback must not erase the attempted publication."""
        state.update(error=error.code if isinstance(error, DeploymentError) else "INTERRUPTED",
                     rejectedPublication=state["candidate"]["publicationId"])
        if not policy["rollback"]:
            state.update(phase="recoveryRequired", finishedAt=self.clock())
            self.store.save(state)
            raise DeploymentError("RECOVERY_REQUIRED") from None
        try:
            self.rollback(state)
        except (DeploymentError, InterruptedError):
            state.update(phase="recoveryRequired", error="ROLLBACK_FAILED", finishedAt=self.clock())
            self.store.save(state)
            raise DeploymentError("ROLLBACK_FAILED") from None
        raise DeploymentError("PUBLICATION_ROLLED_BACK") from None

    def rollback(self, state):
        """Restore application images only, verifying history before any replacement."""
        ensure(self.runtime.history() == identifiers(state["previous"]["migrationCatalog"]), "DATABASE_HISTORY_DIVERGED")
        self.transition(state, "rollingBack")
        self.runtime.stop()
        self.runtime.start(state["previous"])
        self.transition(state, "verifyingRollback")
        state["checks"] = self.runtime.smoke(state["previous"])
        ensure(self.runtime.history() == identifiers(state["previous"]["migrationCatalog"]), "DATABASE_HISTORY_DIVERGED")
        state.update(current=state["previous"], phase="rolledBack", finishedAt=self.clock())
        self.store.finish(state)
