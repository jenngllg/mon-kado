"""Pin independently installed coordination and backup code to a reviewed release."""

import hashlib
import json
from pathlib import Path
import re
import stat

from deploy_policy import ensure
from deploy_storage import private_read

COORDINATOR_FILES = (
    "src/Operations.Deployment/coordinated_backend.py",
    "src/Operations.Deployment/coordinated_backup.py",
    "src/Operations.Deployment/coordinated_boot.py",
    "src/Operations.Deployment/coordinated_bootstrap.py",
    "src/Operations.Deployment/coordinated_cli.py",
    "src/Operations.Deployment/coordinated_engine.py",
    "src/Operations.Deployment/coordinated_frontend.py",
    "src/Operations.Deployment/coordinated_gate.py",
    "src/Operations.Deployment/coordinated_integrity.py",
    "src/Operations.Deployment/coordinated_install_engine.py",
    "src/Operations.Deployment/coordinated_install_cli.py",
    "src/Operations.Deployment/coordinated_install_files.py",
    "src/Operations.Deployment/coordinated_install_policy.py",
    "src/Operations.Deployment/coordinated_install_runtime.py",
    "src/Operations.Deployment/coordinated_locks.py",
    "src/Operations.Deployment/coordinated_package.py",
    "src/Operations.Deployment/coordinated_policy.py",
    "src/Operations.Deployment/coordinated_runtime.py",
    "src/Operations.Deployment/coordinated_store.py",
    "src/Operations.Deployment/coordinated_swap.py",
    "deployments/production/monkado-transition-guard.service",
    "deployments/production/monkado-coordinate",
)
BACKUP_MODULES = ("__init__.py", "policy.py", "capture.py", "operations.py", "cli.py", "restore.py", "credentials.py")
BACKUP_FILES = tuple("src/Operations.Backups/monkado_backup/" + item for item in BACKUP_MODULES)
FILES = COORDINATOR_FILES + BACKUP_FILES
MAXIMUM_FILE_BYTES = 2 * 1024 * 1024


def content_digest(path):
    """Hash one bounded regular file without following any path symlink."""
    path = Path(path)
    ensure(path.is_absolute() and path.resolve() == path and path.is_file() and not path.is_symlink(), "TOOL_FILE_INVALID")
    ensure(path.stat().st_size <= MAXIMUM_FILE_BYTES, "TOOL_FILE_INVALID")
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def create(source, revision, configuration, archive_digest):
    """Build fixed inventory data; this does not itself approve a publication."""
    source = Path(source).absolute()
    value = {"schemaVersion": 1, "revision": revision, "configurationHash": configuration, "archiveSha256": archive_digest,
             "files": {name: content_digest(source / name) for name in FILES}}
    validate(value, configuration)
    return value


def validate(value, configuration):
    """Reject unknown files, revisions, hashes and changed host configuration."""
    ensure(isinstance(value, dict) and set(value) == {"schemaVersion", "revision", "configurationHash", "archiveSha256", "files"},
           "TOOL_MANIFEST_INVALID")
    ensure(type(value["schemaVersion"]) is int and value["schemaVersion"] == 1 and
           isinstance(value["revision"], str) and re.fullmatch(r"[0-9a-f]{40}", value["revision"]) is not None and
           isinstance(configuration, str) and re.fullmatch(r"[0-9a-f]{64}", configuration) is not None and
           value["configurationHash"] == configuration and
           isinstance(value["archiveSha256"], str) and re.fullmatch(r"[0-9a-f]{64}", value["archiveSha256"]) is not None,
           "TOOL_MANIFEST_INVALID")
    files = value["files"]
    ensure(isinstance(files, dict) and set(files) == set(FILES) and
           all(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest) is not None for digest in files.values()),
           "TOOL_MANIFEST_INVALID")
    return value


def digest(value):
    """Bind the exact complete manifest to durable transition intent."""
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest()


def safe_parents(path, base):
    """Refuse writable or linked directories beneath a trusted installation root."""
    path, base = Path(path), Path(base)
    ensure(path.is_absolute() and base.is_absolute() and (path == base or base in path.parents), "TOOL_FILE_INVALID")
    current = path
    while True:
        info = current.lstat()
        ensure(stat.S_ISDIR(info.st_mode) and current.resolve() == current and info.st_uid == 0 and info.st_gid == 0 and
               not info.st_mode & 0o022, "TOOL_FILE_INVALID")
        if current == base:
            return
        current = current.parent


def verify_installed(value, configuration, root, backup_root, unit, requires):
    """Verify reviewed bytes, ownership and the Caddy boot-guard dependency."""
    validate(value, configuration)
    root, backup_root, unit, requires = map(Path, (root, backup_root, unit, requires))
    for name in FILES:
        path = root / name if name in COORDINATOR_FILES else backup_root / Path(name).name
        safe_parents(path.parent, root if name in COORDINATOR_FILES else backup_root)
        ensure(path.resolve() == path and path.is_file() and not path.is_symlink(), "TOOL_FILE_INVALID")
        info = path.stat()
        expected_mode = 0o755 if name == "deployments/production/monkado-coordinate" else 0o644
        ensure(info.st_uid == 0 and info.st_gid == 0 and stat.S_IMODE(info.st_mode) == expected_mode,
               "TOOL_FILE_INVALID")
        ensure(content_digest(path) == value["files"][name], "TOOL_FILE_CHANGED")
    service_name = "deployments/production/monkado-transition-guard.service"
    safe_parents(unit.parent, unit.parent)
    safe_parents(requires.parent, unit.parent)
    ensure(unit.is_file() and not unit.is_symlink() and unit.resolve() == unit, "TOOL_UNIT_INVALID")
    info = unit.stat()
    ensure(info.st_uid == 0 and info.st_gid == 0 and stat.S_IMODE(info.st_mode) == 0o644 and
           content_digest(unit) == value["files"][service_name], "TOOL_UNIT_INVALID")
    ensure(requires.is_symlink() and requires.resolve() == unit, "TOOL_UNIT_INVALID")
    return digest(value)


def read_manifest(path):
    """Read a bounded root-only manifest installed after protected approval."""
    return json.loads(private_read(Path(path), 65536))
