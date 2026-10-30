"""Install or resume one protected coordination bundle on the native host."""

from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys

from coordinated_backup import capture, run as backup_command
from coordinated_cli import require_protected_tool
from coordinated_gate import Gate
from coordinated_install_engine import InstallationEngine
from coordinated_install_runtime import InstallationRuntime
from coordinated_integrity import read_manifest
from coordinated_locks import locks
from coordinated_store import InstallationStore
from deploy_policy import DeploymentError, ensure
from deploy_runtime import Runtime
from deploy_storage import Store
import frontend_runtime as hosting
from release_manifest import configuration_hash

ROOT = Path("/opt/monkado")
STATE = Path("/var/lib/monkado-deployment")
FRONTEND = Path("/var/lib/monkado-frontend")
SETTINGS = Path("/etc/monkado")
BACKUP_ROOT = Path("/opt/monkado-backup")
SYSTEMD = Path("/etc/systemd/system")


def now():
    """Return UTC for backup freshness checks."""
    return datetime.now(timezone.utc)


def source_manifest(source):
    """Read only a private root-owned, already verified staging directory."""
    source = Path(source)
    info = source.lstat()
    ensure(source.is_absolute() and source.resolve() == source and source.is_dir() and not source.is_symlink() and
           info.st_uid == 0 and info.st_gid == 0 and info.st_mode & 0o777 == 0o700, "TOOL_SOURCE_INVALID")
    return read_manifest(source / "approved-manifest.json")


def runtime(source, started):
    """Wire installed providers with the reviewed source tree and native locks."""
    native = Runtime()
    publisher = hosting.Deployment(FRONTEND, STATE, configuration_hash(ROOT))
    return InstallationRuntime(source, ROOT, STATE, SETTINGS, BACKUP_ROOT, SYSTEMD, Store(STATE), native, publisher,
                               Gate(), lambda: configuration_hash(ROOT), require_protected_tool,
                               lambda: json.loads(backup_command("status")), started, clock=now)


def install(source):
    """Capture a fresh backup before starting the protected first installation."""
    manifest = source_manifest(source)
    require_protected_tool(manifest)
    store = InstallationStore(STATE)
    ensure(store.load() is None, "TOOL_INSTALL_RECOVERY_REQUIRED")
    started = now()
    snapshot = capture(clock=now)
    with locks(STATE):
        result = InstallationEngine(store, runtime(Path(source), started)).install(manifest, snapshot)
    return {"state": "installed", **result}


def recover(source):
    """Resume only the journal-bound installation from the same verified source."""
    manifest = source_manifest(source)
    store = InstallationStore(STATE)
    with locks(STATE):
        intent = store.load()
        ensure(intent is not None and intent.get("manifest") == manifest, "TOOL_INSTALL_RECOVERY_REQUIRED")
        result = InstallationEngine(store, runtime(Path(source), now())).recover()
    return {"state": "installed", **result}


def main():
    """Emit only bounded technical diagnostics, never provider output."""
    try:
        ensure(os.geteuid() == 0 and len(sys.argv) == 3 and sys.argv[1] in ("install", "recover"), "INVALID_ARGUMENTS")
        value = install(sys.argv[2]) if sys.argv[1] == "install" else recover(sys.argv[2])
        print(json.dumps(value, sort_keys=True))
        return 0
    except Exception as error:
        code = error.code if isinstance(error, DeploymentError) else "TOOL_INSTALL_FAILED"
        print(json.dumps({"state": "failed", "code": code}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
