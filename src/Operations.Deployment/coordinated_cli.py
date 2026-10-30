"""Root-only administration for a reviewed coordinated pair transition."""

from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys
import tempfile

from coordinated_backend import CoordinatedBackend
from coordinated_backup import capture, run as backup_command
from coordinated_engine import CoordinatedEngine
from coordinated_frontend import CoordinatedFrontend
from coordinated_gate import Gate
from coordinated_integrity import read_manifest, validate as validate_tool, verify_installed
from coordinated_locks import locks
from coordinated_runtime import CoordinatedRuntime
from coordinated_store import CoordinatedStore
from deploy_cli import approved_release
from deploy_policy import DeploymentError, ensure
from deploy_process import command
from deploy_runtime import Runtime
from deploy_storage import Store
import frontend_runtime as hosting
from release_manifest import configuration_hash

ROOT = Path("/opt/monkado")
STATE = Path("/var/lib/monkado-deployment")
FRONTEND = Path("/var/lib/monkado-frontend")
BACKUP_CODE = Path("/opt/monkado-backup/monkado_backup")
TOOL_MANIFEST = Path("/etc/monkado/coordinated-manifest.json")
UNIT = Path("/etc/systemd/system/monkado-transition-guard.service")
REQUIRES = Path("/etc/systemd/system/caddy.service.requires/monkado-transition-guard.service")
TOOL_POINTER = "https://api.github.com/repos/jenngllg/mon-kado/releases/tags/coordinator-production"


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


def require_protected_tool(manifest):
    """Require the installed manifest to equal the protected public channel."""
    raw = command(["curl", "--disable", "--fail", "--silent", "--show-error", "--location", "--max-redirs", "0",
                   "--proto", "=https", "--proto-redir", "=https", "--noproxy", "*", "--connect-timeout", "10",
                   "--max-time", "30", "--max-filesize", "131072", "--header", "Accept: application/vnd.github+json",
                   TOOL_POINTER], maximum=131072)
    release = json.loads(raw)
    ensure(isinstance(release, dict) and release.get("tag_name") == "coordinator-production" and
           release.get("draft") is False and release.get("prerelease") is False and
           isinstance(release.get("body"), str) and len(release["body"]) <= 65536, "TOOL_APPROVAL_INVALID")
    value = validate_tool(json.loads(release["body"]), configuration_hash(ROOT))
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
        runtime, _, _ = make_runtime(now())
        result = CoordinatedEngine(store, runtime).recover()
    return {"state": result}


def main():
    """Expose bounded fixed diagnostics and no provider responses or private data."""
    try:
        ensure(os.geteuid() == 0 and sys.argv[1:] in (["deploy"], ["recover"]), "INVALID_ARGUMENTS")
        value = deploy() if sys.argv[1] == "deploy" else recover()
        print(json.dumps(value, sort_keys=True))
        return 0
    except Exception as error:
        code = error.code if isinstance(error, DeploymentError) else "COORDINATED_OPERATION_FAILED"
        print(json.dumps({"state": "failed", "code": code}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
