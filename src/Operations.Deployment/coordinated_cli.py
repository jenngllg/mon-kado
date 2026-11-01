"""Root-only administration for a reviewed coordinated pair transition."""

from datetime import datetime, timezone
import json
import hashlib
import os
import re
from pathlib import Path
import sys
import tempfile

from coordinated_backend import CoordinatedBackend
from coordinated_backup import capture, run as backup_command
from coordinated_engine import CoordinatedEngine, ForwardEngine
from coordinated_frontend import CoordinatedFrontend
from coordinated_gate import Gate
from coordinated_integrity import read_manifest, validate as validate_tool, verify_installed
from coordinated_locks import locks
from coordinated_runtime import CoordinatedRuntime, ForwardRuntime
from coordinated_store import CoordinatedStore
from deploy_cli import approved_release
from deploy_policy import DeploymentError, ensure
from deploy_process import command
from deploy_runtime import Runtime, OnlineRuntime, online_compatible
from deploy_storage import Store, private_read, atomic_write
from deploy_engine import Engine
import frontend_runtime as hosting
from release_manifest import configuration_hash
from smoke_functional import FunctionalSmoke

ROOT = Path("/opt/monkado")
STATE = Path("/var/lib/monkado-deployment")
FRONTEND = Path("/var/lib/monkado-frontend")
BACKUP_CODE = Path("/opt/monkado-backup/monkado_backup")
TOOL_MANIFEST = Path("/etc/monkado/coordinated-manifest.json")
UNIT = Path("/etc/systemd/system/monkado-transition-guard.service")
REQUIRES = Path("/etc/systemd/system/caddy.service.requires/monkado-transition-guard.service")
TOOL_POINTER = "https://api.github.com/repos/jenngllg/mon-kado/releases/tags/coordinator-production"


class HistoricalSmoke(FunctionalSmoke):
    """Inspect a retained pre-boolean API only during a verified forward transition.

    The candidate's real post-migration smoke always uses FunctionalSmoke and
    requires isAdmin. This reader does not affect application authorization.
    """

    def verify_identity(self, current):
        if "isAdmin" in current:
            return super().verify_identity(current)
        ensure(current.get("id") == self.account["memberId"] and current.get("roles") == ["Member"],
               "SMOKE_IDENTITY_REFUSED")


def now():
    """Return an explicit UTC time for the fresh backup boundary."""
    return datetime.now(timezone.utc)


def installed_tool():
    """Recheck all code and the boot guard before any coordinated effect."""
    install_intent = STATE / "coordinated-install.json"
    ensure(not os.path.lexists(install_intent), "TOOL_INSTALL_RECOVERY_REQUIRED")
    configuration = configuration_hash(ROOT)
    manifest = read_manifest(TOOL_MANIFEST)
    tool_digest = verify_installed(manifest, configuration, ROOT, BACKUP_CODE, UNIT, REQUIRES)
    Runtime().run(["systemctl", "is-active", "--quiet", "monkado-transition-guard.service"], timeout=15)
    return manifest, tool_digest


def require_protected_tool(manifest, configuration=None):
    """Require the installed manifest to equal the protected public channel."""
    raw = command(["curl", "--disable", "--fail", "--silent", "--show-error", "--location", "--max-redirs", "0",
                   "--proto", "=https", "--proto-redir", "=https", "--noproxy", "*", "--connect-timeout", "10",
                   "--max-time", "30", "--max-filesize", "131072", "--header", "Accept: application/vnd.github+json",
                   TOOL_POINTER], maximum=131072)
    release = json.loads(raw)
    ensure(isinstance(release, dict) and release.get("tag_name") == "coordinator-production" and
           release.get("draft") is False and release.get("prerelease") is False and
           isinstance(release.get("body"), str) and len(release["body"]) <= 65536, "TOOL_APPROVAL_INVALID")
    value = validate_tool(json.loads(release["body"]), configuration or configuration_hash(ROOT))
    ensure(value == manifest, "TOOL_APPROVAL_CHANGED")


def approved_pairs(publisher, backend_store):
    """Resolve exact existing and protected candidate pairs, including immutable proof."""
    state = backend_store.load()
    ensure(state["phase"] in {"succeeded", "acknowledged"} and state["current"] is not None, "BACKEND_CHANGED")
    old_backend = state["current"]
    old_revision = hosting.current_revision(publisher.releases)
    ensure(old_revision is not None, "FRONTEND_NOT_HEALTHY")
    old_frontend = publisher.manifest(old_revision, old_backend["revision"])
    new_backend = approved_release(configuration_hash(ROOT))
    with tempfile.TemporaryDirectory(prefix="coordinated-pair-", dir=publisher.root) as temporary:
        staging = Path(temporary)
        new_frontend = publisher.approved(staging, new_backend["revision"])
        proof_url = (f'https://github.com/{hosting.contract.REPOSITORY}/releases/download/'
                     f'frontend-{new_frontend["revision"]}/coordinated-proof.json')
        proof_path = staging / "coordinated-proof.json"
        publisher.transport(proof_url, proof_path, 4096)
        ensure(proof_path.is_file() and not proof_path.is_symlink() and proof_path.stat().st_size <= 4096,
               "FRONTEND_PROOF_INVALID")
        proof = json.loads(proof_path.read_text(encoding="utf-8"))
    return ({"backend": old_backend, "frontend": old_frontend},
            {"backend": new_backend, "frontend": new_frontend, "proof": proof})


def make_runtime(started):
    """Wire native implementations without loading credentials into arguments or logs."""
    gate = Gate()
    backend_store = Store(STATE)
    native = Runtime()
    publisher = hosting.Deployment(FRONTEND, STATE, configuration_hash(ROOT))
    frontend = CoordinatedFrontend(publisher, gate)
    backend = CoordinatedBackend(backend_store, native, frontend, gate,
                                 lambda: now().isoformat(), lambda: approved_release(configuration_hash(ROOT)))
    return CoordinatedRuntime(backend, frontend, gate, lambda: configuration_hash(ROOT),
                              lambda: installed_tool()[1], lambda: json.loads(backup_command("status")), started), publisher, backend_store


def make_forward_runtime(started):
    """Use a historical preflight reader and the strict boolean candidate smoke."""
    gate = Gate()
    backend_store = Store(STATE)
    native = Runtime(preflight_smoke_factory=HistoricalSmoke)
    publisher = hosting.Deployment(FRONTEND, STATE, configuration_hash(ROOT))
    frontend = CoordinatedFrontend(publisher, gate)
    backend = CoordinatedBackend(backend_store, native, frontend, gate,
                                 lambda: now().isoformat(), lambda: approved_release(configuration_hash(ROOT)))
    runtime = ForwardRuntime(backend, frontend, gate, lambda: configuration_hash(ROOT),
                             lambda: installed_tool()[1], lambda: json.loads(backup_command("status")), started)
    return runtime, publisher, backend_store


def approved_forward_pairs(publisher, backend_store):
    """Retain the original frontend approval and resolve one protected new pair."""
    state = backend_store.load()
    ensure(state["phase"] in {"succeeded", "acknowledged"} and state["current"] is not None, "BACKEND_CHANGED")
    previous = CoordinatedFrontend(publisher, Gate()).historical_pair(state["current"])
    backend = approved_release(configuration_hash(ROOT))
    with tempfile.TemporaryDirectory(prefix="forward-pair-", dir=publisher.root) as temporary:
        staging = Path(temporary)
        pointer = staging / "frontend-pointer.json"
        publisher.transport("https://api.github.com/repos/jenngllg/mon-kado-front/releases/tags/frontend-production", pointer, 131072)
        release = json.loads(pointer.read_text(encoding="utf-8"))
        binding = json.loads(release["body"])["backendRevision"]
        supplied = json.loads(release["body"])
        expected_configuration = previous["frontend"]["configurationHash"] if supplied == previous["frontend"] else configuration_hash(ROOT)
        frontend = hosting.contract.from_release(release, expected_configuration, binding)
        ensure(frontend == previous["frontend"] or binding == backend["revision"], "FRONTEND_RELEASE_REQUIRED")
        proof = staging / "proof.json"
        url = f'https://github.com/{hosting.contract.REPOSITORY}/releases/download/frontend-{frontend["revision"]}/coordinated-proof.json'
        publisher.transport(url, proof, 4096)
        candidate = {"backend": backend, "frontend": frontend, "proof": json.loads(proof.read_text(encoding="utf-8"))}
    return previous, candidate


def forward_deploy():
    """Back up, authenticate both pairs, then close traffic for a forward migration."""
    manifest, _ = installed_tool()
    require_protected_tool(manifest)
    store = CoordinatedStore(STATE)
    ensure(store.load() is None and not Gate().exists(), "RECOVERY_REQUIRED")
    started = now()
    snapshot = capture(clock=now)
    with locks(STATE):
        ensure(store.load() is None and not Gate().exists(), "RECOVERY_REQUIRED")
        runtime, publisher, backend_store = make_forward_runtime(started)
        previous, candidate = approved_forward_pairs(publisher, backend_store)
        result = ForwardEngine(store, runtime).deploy(previous, candidate, snapshot)
    return {"state": result, "backendSnapshot": snapshot}


def github_bytes(url, maximum=131072):
    """Read a fixed public GitHub resource without redirects, proxies or credentials."""
    return command(["curl", "--disable", "--fail", "--silent", "--show-error", "--max-redirs", "0",
                    "--proto", "=https", "--noproxy", "*", "--connect-timeout", "10", "--max-time", "30",
                    "--max-filesize", str(maximum), "--header", "Accept: application/vnd.github+json", url], maximum=maximum)


def main_head(repository):
    """Resolve only the two fixed repositories' protected production branch."""
    ensure(repository in {"mon-kado", "mon-kado-front"}, "INVALID_REPOSITORY")
    value = json.loads(github_bytes(f"https://api.github.com/repos/jenngllg/{repository}/git/ref/heads/main"))
    revision = value["object"]["sha"]
    ensure(isinstance(revision, str) and re.fullmatch(r"[0-9a-f]{40}", revision), "INVALID_REVISION")
    return revision


def verify_candidate_schema(candidate):
    """Refuse incompatible retained frontends before any migration or traffic closure."""
    revision = candidate["backend"]["revision"]
    ensure(isinstance(revision, str) and re.fullmatch(r"[0-9a-f]{40}", revision), "INVALID_REVISION")
    body = github_bytes(f"https://raw.githubusercontent.com/jenngllg/mon-kado/{revision}/internal/httpapi/specification.json", 2 * 1024 * 1024)
    ensure(hashlib.sha256(body).hexdigest() == candidate["proof"]["openapiSha256"], "FRONTEND_RELEASE_REQUIRED")
    return json.loads(body)


def main_deploy(component, revision):
    """Serve a forced-command CI request only for an approved current main revision."""
    ensure(component in {"backend", "frontend"} and isinstance(revision, str) and
           re.fullmatch(r"[0-9a-f]{40}", revision), "INVALID_ARGUMENTS")
    manifest, _ = installed_tool()
    require_protected_tool(manifest)
    ensure(CoordinatedStore(STATE).load() is None and not Gate().exists(), "RECOVERY_REQUIRED")
    pending = STATE / "online-transition.json"
    if pending.exists():
        pinned = json.loads(private_read(pending))
        repository = "mon-kado" if component == "backend" else "mon-kado-front"
        ensure(pinned["candidate"][component]["revision"] == revision == main_head(repository), "ONLINE_RECOVERY_REQUIRED")
        return online_recover()
    _, publisher, backend_store = make_forward_runtime(now())
    previous, candidate = approved_forward_pairs(publisher, backend_store)
    repository = "mon-kado" if component == "backend" else "mon-kado-front"
    ensure(candidate[component]["revision"] == revision == main_head(repository), "MAIN_REVISION_CHANGED")
    # CI waits outside the VPS lock. Refuse a late merge before taking any backup;
    # the request for that newer checked publication will select the complete pair.
    ensure(candidate["backend"]["revision"] == main_head("mon-kado") and
           candidate["frontend"]["revision"] == main_head("mon-kado-front"), "PAIRED_PUBLICATION_REQUIRED")
    for name, repo in (("backend", "mon-kado"), ("frontend", "mon-kado-front")):
        if candidate[name] != previous[name]:
            ensure(candidate[name]["revision"] == main_head(repo), "MAIN_REVISION_CHANGED")
    # An existing frontend keeps its original immutable approval. The overlapping
    # old/new APIs must preserve every operation/schema used by that frontend.
    new_schema = json.loads(github_bytes(f'https://raw.githubusercontent.com/jenngllg/mon-kado/{candidate["backend"]["revision"]}/internal/httpapi/specification.json', 2 * 1024 * 1024))
    old_status, _, old_body = Runtime().clients().request("GET", "/openapi/v1.json")
    ensure(old_status == 200, "LIVE_API_NOT_READY")
    online_compatible(json.loads(old_body), new_schema)
    if candidate["frontend"] == previous["frontend"]:
        ensure(candidate["proof"] == previous["proof"], "FRONTEND_APPROVAL_CHANGED")
    else:
        verify_candidate_schema(candidate)
    ensure(candidate["backend"]["migrationCatalog"] == previous["backend"]["migrationCatalog"], "ONLINE_SCHEMA_REVIEW_REQUIRED")
    if candidate["backend"] == previous["backend"] and candidate["frontend"] == previous["frontend"]:
        return {"state": "alreadyInstalled"}
    return online_deploy(previous, candidate)


class OnlineGate:
    """Verify availability without ever closing the public HTTP/HTTPS ports."""

    def verify(self):
        ensure(not Gate().exists(), "TRAFFIC_GATE_CLOSED")
        Runtime().run(["systemctl", "is-active", "--quiet", "mon-kado-api.service", "caddy.service"], timeout=15)

    def exists(self):
        return Gate().exists()

    def close(self):
        self.verify()

    def open(self):
        self.verify()


def online_deploy(previous, candidate):
    """Back up and replace a schema-compatible pair while public traffic keeps serving."""
    path = STATE / "online-transition.json"
    ensure(not path.exists(), "ONLINE_RECOVERY_REQUIRED")
    started = now()
    snapshot = capture(clock=now)
    backend_changes = candidate["backend"] != previous["backend"]
    intent = {"version": 2, "phase": "prepared", "startedAt": started.isoformat(), "previous": previous, "candidate": candidate,
              "backendSnapshot": snapshot if backend_changes else None,
              "frontendSnapshot": snapshot if candidate["frontend"] != previous["frontend"] else None}
    with locks(STATE):
        ensure(not path.exists(), "ONLINE_RECOVERY_REQUIRED")
        atomic_write(path, json.dumps(intent, sort_keys=True).encode())
        return complete_online(intent)


def complete_online(intent):
    """Resume only the pinned pair; all serving changes have explicit health checks."""
    from release_manifest import validate
    from coordinated_policy import validate_backup
    path = STATE / "online-transition.json"
    ensure(isinstance(intent, dict) and set(intent) == {"version", "phase", "startedAt", "previous", "candidate", "backendSnapshot", "frontendSnapshot"}
           and type(intent["version"]) is int and intent["version"] in (1, 2) and
           intent["phase"] in {"prepared", "backendInstalled", "frontendInstalled", "committed"}, "ONLINE_INTENT_INVALID")
    previous, candidate = intent["previous"], intent["candidate"]
    validate(candidate["backend"], configuration_hash(ROOT))
    hosting.contract.validate(candidate["frontend"], candidate["frontend"]["configurationHash"], candidate["frontend"]["backendRevision"])
    ensure(all(value is None or isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value)
               for value in (intent["backendSnapshot"], intent["frontendSnapshot"])), "BACKUP_REFERENCE_REQUIRED")
    if intent["version"] == 2:
        references = {value for value in (intent["backendSnapshot"], intent["frontendSnapshot"]) if value is not None}
        ensure(len(references) == 1 and
               (intent["backendSnapshot"] is not None) == (candidate["backend"] != previous["backend"]) and
               (intent["frontendSnapshot"] is not None) == (candidate["frontend"] != previous["frontend"]), "BACKUP_REFERENCE_REQUIRED")
    ensure(previous["backend"]["migrationCatalog"] == candidate["backend"]["migrationCatalog"], "ONLINE_SCHEMA_REVIEW_REQUIRED")
    runtime = OnlineRuntime()
    gate = OnlineGate()
    gate.verify()
    publisher = hosting.Deployment(FRONTEND, STATE, configuration_hash(ROOT))
    frontend = CoordinatedFrontend(publisher, gate)
    backend_store = Store(STATE)
    observed = backend_store.load()
    ensure(observed["current"] in (previous["backend"], candidate["backend"]), "BACKEND_CHANGED")
    if observed["phase"] not in {"succeeded", "acknowledged", "rolledBack", "rejected"}:
        ensure(observed["candidate"] == candidate["backend"] and observed["previous"] == previous["backend"], "ONLINE_RECOVERY_REQUIRED")
        Engine(backend_store, runtime, lambda: now().isoformat()).rollback(observed)
        intent["phase"] = "rolledBack"
        atomic_write(STATE / ("online-failed-" + candidate["backend"]["publicationId"] + ".json"), json.dumps(intent).encode())
        path.unlink()
        raise DeploymentError("PUBLICATION_ROLLED_BACK")
    if intent["phase"] == "prepared" and previous["backend"] != candidate["backend"] and observed["current"] == candidate["backend"]:
        intent["phase"] = "backendInstalled"
        atomic_write(path, json.dumps(intent, sort_keys=True).encode())
    if candidate["frontend"] != previous["frontend"] and intent["phase"] == "prepared":
        frontend.prepare_forward(previous, candidate)
    if intent["phase"] == "prepared":
        snapshot = intent["backendSnapshot"] or intent["frontendSnapshot"]
        validate_backup(json.loads(backup_command("status")), snapshot, now(), datetime.fromisoformat(intent["startedAt"]))
        state = backend_store.load()
        if state["current"] != candidate["backend"]:
            ensure(state["current"] == previous["backend"], "BACKEND_CHANGED")
            result = Engine(backend_store, runtime, lambda: now().isoformat()).deploy(candidate["backend"])
            ensure(result["phase"] == "succeeded", "BACKEND_VERIFICATION_FAILED")
        intent["phase"] = "backendInstalled"
        atomic_write(path, json.dumps(intent, sort_keys=True).encode())
    if candidate["frontend"] != previous["frontend"] and intent["phase"] == "backendInstalled":
        if intent["version"] == 2:
            # Both unchanged-schema switches belong to the same pinned operation.
            # Recovery must revalidate that operation's backup rather than reuse
            # an unrelated capture or silently take another after the API switch.
            validate_backup(json.loads(backup_command("status")), intent["frontendSnapshot"], now(), datetime.fromisoformat(intent["startedAt"]))
        if intent["frontendSnapshot"] is None:
            intent["frontendSnapshot"] = capture(clock=now, inherited=True)
            atomic_write(path, json.dumps(intent, sort_keys=True).encode())
        frontend.activate_forward(previous, candidate)
        intent["phase"] = "frontendInstalled"
        atomic_write(path, json.dumps(intent, sort_keys=True).encode())
    gate.verify()
    state = backend_store.load()
    runtime.restore_baseline(state["baseline"])
    runtime.frontend_marker = hosting.contract.release_marker(candidate["frontend"]["revision"], candidate["frontend"].get("googleEnabled", False))
    if previous["backend"] == candidate["backend"]:
        ensure(runtime.smoke(candidate["backend"]) == {"technical": True, "functional": True, "frontend": True}, "BACKEND_VERIFICATION_FAILED")
    else:
        runtime.technical_sample(candidate["backend"])
    if candidate["frontend"] != previous["frontend"]:
        frontend.verify(candidate)
    else:
        frontend.verify_original(previous)
        publisher.health(candidate["frontend"]["revision"], publisher.releases / candidate["frontend"]["revision"], candidate["frontend"].get("googleEnabled", False))
    intent["phase"] = "committed"
    receipt = STATE / ("online-receipt-" + candidate["backend"]["publicationId"] + "-" + candidate["frontend"]["revision"] + ".json")
    atomic_write(receipt, json.dumps(intent, sort_keys=True).encode())
    path.unlink()
    return {"state": "installed", "backendSnapshot": intent["backendSnapshot"], "frontendSnapshot": intent["frontendSnapshot"]}


def online_recover():
    """Reconcile the durable online request without selecting a newer publication."""
    installed_tool()
    with locks(STATE):
        intent = json.loads(private_read(STATE / "online-transition.json"))
        return complete_online(intent)


def forward_recover():
    """Reconcile only the durable candidate while the boot guard keeps traffic closed."""
    installed_tool()
    store = CoordinatedStore(STATE)
    with locks(STATE):
        intent = store.load()
        ensure(intent is not None and intent.get("version") == 2, "FORWARD_INTENT_REQUIRED")
        runtime, _, _ = make_forward_runtime(now())
        result = ForwardEngine(store, runtime).recover()
    return {"state": result}


def deploy():
    """Take a new backend snapshot, then run one protected coordinated transition."""
    manifest, _ = installed_tool()
    require_protected_tool(manifest)
    store = CoordinatedStore(STATE)
    ensure(store.load() is None and not Gate().exists(), "RECOVERY_REQUIRED")
    started = now()
    snapshot = capture(clock=now)
    with locks(STATE):
        ensure(store.load() is None and not Gate().exists(), "RECOVERY_REQUIRED")
        runtime, publisher, backend_store = make_runtime(started)
        previous, candidate = approved_pairs(publisher, backend_store)
        receipt = CoordinatedEngine(store, runtime).deploy(previous, candidate, snapshot)
    return {"state": "installed", **receipt}


def recover():
    """Recover only the journal-bound pair; never require a new network approval."""
    installed_tool()
    store = CoordinatedStore(STATE)
    with locks(STATE):
        intent = store.load()
        if isinstance(intent, dict) and intent.get("version") == 2:
            runtime, _, _ = make_forward_runtime(now())
            result = ForwardEngine(store, runtime).recover()
        else:
            runtime, _, _ = make_runtime(now())
            result = CoordinatedEngine(store, runtime).recover()
    return {"state": result}


def main():
    """Expose bounded fixed diagnostics and no provider responses or private data."""
    try:
        arguments = sys.argv[1:]
        ensure(os.geteuid() == 0, "INVALID_ARGUMENTS")
        if len(arguments) == 3 and arguments[0] == "main-deploy":
            value = main_deploy(arguments[1], arguments[2])
            print(json.dumps(value, sort_keys=True))
            return 0
        ensure(arguments in (["deploy"], ["recover"], ["forward-deploy"], ["forward-recover"], ["online-recover"]), "INVALID_ARGUMENTS")
        operations = {"deploy": deploy, "recover": recover, "forward-deploy": forward_deploy, "forward-recover": forward_recover, "online-recover": online_recover}
        value = operations[sys.argv[1]]()
        print(json.dumps(value, sort_keys=True))
        return 0
    except Exception as error:
        code = error.code if isinstance(error, DeploymentError) else "COORDINATED_OPERATION_FAILED"
        print(json.dumps({"state": "failed", "code": code}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
