"""Root-only local administration; no command accepts credential values as arguments."""

from contextlib import contextmanager
from datetime import datetime, timezone
import fcntl
import json
import os
from pathlib import Path
import signal
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "deployments/production"))
from release_manifest import configuration_hash, from_release, validate
from deploy_engine import Engine
from deploy_policy import DeploymentError, TERMINAL, decision, ensure, initial_state
from deploy_process import command
from deploy_provision import provision
from deploy_runtime import ROOT, SETTINGS, STATE, Runtime
from deploy_storage import Store, private_read
from release_catalog import fingerprint, identifiers, validate_catalog
from smoke_functional import credentials


def now():
    """Record operational timestamps explicitly in UTC."""
    return datetime.now(timezone.utc).isoformat()


@contextmanager
def lock(path, inherited=None):
    """Reuse MK-813's inherited outer lock, otherwise acquire the same coordination lock."""
    descriptor = None
    if inherited is not None:
        try:
            observed = os.fstat(inherited)
            expected = path.stat()
            if (observed.st_dev, observed.st_ino) == (expected.st_dev, expected.st_ino):
                descriptor = os.dup(inherited)
        except OSError:
            pass
    if descriptor is None:
        descriptor = os.open(path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise DeploymentError("COORDINATION_BUSY") from None
        yield
    finally:
        os.close(descriptor)


def interrupt(signum, frame):
    """Let the engine recover once; a second termination remains a hard interruption."""
    signal.signal(signum, signal.SIG_DFL)
    raise InterruptedError()


def summary(state):
    """Expose fixed technical fields rather than the full private state or exceptions."""
    return {"schemaVersion": 1, "phase": state["phase"], "error": state["error"],
            "revision": state["current"]["revision"] if state["current"] else None,
            "publicationId": state["candidate"]["publicationId"] if state["candidate"] else None,
            "startedAt": state["startedAt"], "finishedAt": state["finishedAt"], "checks": state["checks"]}


def validate_saved(state, expected_hash):
    """Revalidate every retained release before any manifest field reaches Docker."""
    for name in ("current", "previous", "candidate"):
        if state[name] is not None:
            validate(state[name], expected_hash)
            ensure(state[name]["schemaVersion"] == 2, "ADOPTION_REQUIRED")


def approved_release(expected_hash, runner=command):
    """Download only the fixed public promotion channel using verified HTTPS."""
    raw = runner(["curl", "--fail", "--silent", "--show-error", "--location", "--proto", "=https", "--proto-redir", "=https",
                  "--connect-timeout", "10", "--max-time", "30", "--max-filesize", "65536",
                  "https://api.github.com/repos/jenngllg/mon-kado/releases/tags/backend-production"], maximum=65536)
    release = from_release(json.loads(raw), expected_hash)
    ensure(release["schemaVersion"] == 2, "PUBLICATION_V2_REQUIRED")
    return release


def adopt(catalog_path, store, runtime, expected_hash):
    """Establish a reviewed v1 baseline using exact installed images and matching EF history."""
    ensure(not (STATE / "in-progress.env").exists(), "RECOVERY_REQUIRED")
    ensure(not (STATE / "status.json").exists(), "ALREADY_ADOPTED")
    values = dict(line.split("=", 1) for line in private_read(STATE / "current.env").decode().splitlines())
    ensure(set(values) == {"API_IMAGE", "WORKER_IMAGE", "RELEASE_REVISION"}, "INVALID_LEGACY_POINTER")
    catalog = validate_catalog(json.loads(private_read(catalog_path, 32768)))
    value = validate({"schemaVersion": 2, "publicationId": "1-1", "configurationHash": expected_hash,
                      "revision": values["RELEASE_REVISION"], "apiImage": values["API_IMAGE"],
                      "workerImage": values["WORKER_IMAGE"], "migrationCatalog": catalog,
                      "migrationHash": fingerprint(catalog), "rollbackAllowed": False}, expected_hash)
    runtime.verify_active(value)
    for service in ("api", "worker"):
        ensure(runtime.inspect(value[service + "Image"], '{{index .Config.Labels "org.opencontainers.image.revision"}}') == value["revision"],
               "IMAGE_REVISION_MISMATCH")
    ensure(runtime.history() == identifiers(catalog), "DATABASE_HISTORY_DIVERGED")
    baseline = runtime.observe_baseline()
    checks = runtime.smoke(value)
    state = initial_state() | {"phase": "succeeded", "current": value, "checks": checks, "finishedAt": now(), "baseline": baseline}
    store.finish(state)
    return state


def recover(action, publication, store, runtime):
    """Require an exact failed publication and a selected recovery action, never resume migrations."""
    state = store.load()
    candidate = state["candidate"]
    ensure(candidate is not None and candidate["publicationId"] == publication, "RECOVERY_ID_MISMATCH")
    ensure(state["phase"] not in ("idle", "succeeded", "acknowledged"), "RECOVERY_NOT_REQUIRED")
    runtime.restore_baseline(state["baseline"])
    if action == "rollback":
        ensure(state["phase"] != "rejected", "ROLLBACK_UNAVAILABLE")
        ensure(state["previous"] is not None, "ROLLBACK_UNAVAILABLE")
        policy = decision(state["previous"], candidate, runtime.history())
        ensure(policy["rollback"], "ROLLBACK_UNAVAILABLE")
        state["rejectedPublication"] = publication
        state["error"] = "OPERATOR_RECOVERY"
        Engine(store, runtime, now).rollback(state)
    elif action == "accept":
        runtime.verify_active(candidate)
        ensure(runtime.history() == identifiers(candidate["migrationCatalog"]), "DATABASE_HISTORY_DIVERGED")
        checks = runtime.smoke(candidate)
        state.update(phase="succeeded", current=candidate, error=None, rejectedPublication=None, checks=checks, finishedAt=now())
        store.finish(state)
    else:
        ensure(action == "acknowledge" and state["phase"] in ("rolledBack", "rejected"), "INVALID_RECOVERY_ACTION")
        runtime.verify_active(state["current"])
        checks = runtime.smoke(state["current"])
        state.update(phase="acknowledged", error=None, checks=checks, finishedAt=now())
        store.finish(state)
    return state


def rebaseline(store, runtime, expected_hash):
    """Approve a reviewed configuration reinstall without silently rebinding failed publications."""
    state = store.load()
    ensure(state["phase"] in ("succeeded", "acknowledged") and state["current"] is not None, "RECOVERY_REQUIRED")
    current = state["current"]
    validate(current, current["configurationHash"])
    ensure(current["schemaVersion"] == 2, "ADOPTION_REQUIRED")
    runtime.verify_active(current)
    ensure(runtime.history() == identifiers(current["migrationCatalog"]), "DATABASE_HISTORY_DIVERGED")
    baseline = runtime.observe_baseline()
    checks = runtime.smoke(current)
    state.update(current=current | {"configurationHash": expected_hash}, previous=None, candidate=None,
                 baseline=baseline, checks=checks, finishedAt=now())
    store.finish(state)
    return state


def execute(arguments):
    """Keep operational writes serialized with backups, including operator recovery and adoption."""
    ensure(os.geteuid() == 0, "ROOT_REQUIRED")
    os.umask(0o077)
    store = Store(STATE)
    if arguments == ["status"]:
        return summary(store.load())
    expected_hash = configuration_hash(ROOT)
    if arguments == ["check-config"]:
        private_read(SETTINGS / "production.env")
        credentials(SETTINGS / "deployment-smoke.json")
        return {"configurationValid": True}
    ensure(arguments == ["deploy"] or arguments in (["smoke-readonly"], ["smoke-write"], ["provision-smoke"], ["rebaseline"]) or
           len(arguments) == 2 and arguments[0] == "adopt" or len(arguments) == 3 and arguments[0] == "recover", "INVALID_ARGUMENTS")
    with lock(STATE / "backup-coordination.lock", inherited=8), lock(STATE / "deploy.lock"):
        ensure(not Path("/var/lib/monkado-backup/maintenance.json").exists(), "BACKUP_RECOVERY_REQUIRED")
        runtime = Runtime()
        if arguments == ["provision-smoke"]:
            return provision(SETTINGS, STATE, runtime.clients)
        if arguments == ["rebaseline"]:
            return summary(rebaseline(store, runtime, expected_hash))
        if arguments[0] == "adopt":
            return summary(adopt(Path(arguments[1]), store, runtime, expected_hash))
        state = store.load()
        validate_saved(state, expected_hash)
        if arguments[0] == "deploy":
            signal.signal(signal.SIGTERM, interrupt)
            signal.signal(signal.SIGINT, interrupt)
            candidate = approved_release(expected_hash)
            ensure(state["phase"] in TERMINAL, "RECOVERY_REQUIRED")
            ensure(state["current"] is not None, "ADOPTION_REQUIRED")
            if candidate["publicationId"] == state["rejectedPublication"]:
                runtime.verify_active(state["current"])
                store.reconcile(state)
                return summary(state)
            return summary(Engine(store, runtime, now).deploy(candidate))
        if arguments[0] == "recover":
            return summary(recover(arguments[1], arguments[2], store, runtime))
        ensure(state["current"] is not None, "ADOPTION_REQUIRED")
        runtime.restore_baseline(state["baseline"])
        if arguments == ["smoke-write"]:
            return runtime.smoke(state["current"])
        runtime.technical_sample(state["current"])
        return {"technical": True}


def main(arguments):
    """Report fixed error codes only, suppressing subprocess output and provider exception text."""
    try:
        print(json.dumps(execute(arguments), sort_keys=True))
        return 0
    except (DeploymentError, OSError, ValueError, KeyError, TypeError, InterruptedError) as error:
        code = error.code if isinstance(error, DeploymentError) else "DEPLOYMENT_OPERATION_FAILED"
        print(json.dumps({"event": "deployment_failed", "code": code}))
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
