"""Idempotently stage the reviewed coordinator without overwriting historical sources."""

import json
import os
from pathlib import Path
import stat
import tempfile

from coordinated_integrity import BACKUP_FILES, BACKUP_MODULES, COORDINATOR_FILES, content_digest, safe_parents
from coordinated_swap import exchange
from deploy_policy import ensure
from deploy_storage import sync_directory


def directory(base, relative):
    """Create only missing root-owned directories under an existing trusted base."""
    base = Path(base)
    safe_parents(base, base)
    current = base
    for part in Path(relative).parts:
        ensure(part not in ("", ".", "..") and "/" not in part, "TOOL_INSTALL_PATH_INVALID")
        current = current / part
        if not current.exists() and not current.is_symlink():
            current.mkdir(mode=0o755)
            sync_directory(current.parent)
        safe_parents(current, base)
    return current


def install_new(path, data, mode):
    """Create a reviewed file once, or accept only an identical safe existing file."""
    path = Path(path)
    if path.exists() or path.is_symlink():
        info = path.lstat()
        ensure(stat.S_ISREG(info.st_mode) and path.resolve() == path and info.st_uid == 0 and info.st_gid == 0 and
               stat.S_IMODE(info.st_mode) == mode and info.st_size == len(data), "TOOL_INSTALL_FILE_CHANGED")
        ensure(path.read_bytes() == data, "TOOL_INSTALL_FILE_CHANGED")
        return
    descriptor, temporary = tempfile.mkstemp(prefix=".coordinator-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            os.fchmod(stream.fileno(), mode)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        # A hard link is an atomic create-if-absent; never replace an unrelated file.
        os.link(temporary, path, follow_symlinks=False)
        sync_directory(path.parent)
    finally:
        Path(temporary).unlink(missing_ok=True)


def stage_tool(source, manifest, root, settings):
    """Install only new reviewed paths and retain all previous backup sources."""
    source, root, settings = map(Path, (source, root, settings))
    for name in COORDINATOR_FILES + BACKUP_FILES:
        source_path = source / name
        ensure(content_digest(source_path) == manifest["files"][name], "TOOL_FILE_CHANGED")
        relative = "src/Operations.Coordinator/backup/" + Path(name).name if name in BACKUP_FILES else name
        parent = directory(root, Path(relative).parent)
        mode = 0o755 if name == "deployments/production/monkado-coordinate" else 0o644
        install_new(parent / Path(relative).name, source_path.read_bytes(), mode)
    safe_parents(settings, settings)
    data = json.dumps(manifest, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")
    install_new(settings / "coordinated-manifest.json", data, 0o600)


def backup_hashes(path):
    """Inspect only the seven fixed root-owned backup modules."""
    path = Path(path)
    safe_parents(path, path)
    result = {}
    for name in BACKUP_MODULES:
        target = path / name
        info = target.lstat()
        ensure(stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_gid == 0 and
               stat.S_IMODE(info.st_mode) == 0o644, "BACKUP_CODE_INVALID")
        result[name] = content_digest(target)
    return result


def stage_guard(source, manifest, systemd):
    """Install the exact reviewed unit and a non-overwriting Caddy dependency."""
    systemd = Path(systemd)
    safe_parents(systemd, systemd)
    name = "deployments/production/monkado-transition-guard.service"
    source_path = Path(source) / name
    ensure(content_digest(source_path) == manifest["files"][name], "TOOL_FILE_CHANGED")
    unit = systemd / "monkado-transition-guard.service"
    install_new(unit, source_path.read_bytes(), 0o644)
    requires = directory(systemd, "caddy.service.requires")
    link = requires / unit.name
    if not link.exists() and not link.is_symlink():
        link.symlink_to(unit)
        sync_directory(requires)
    ensure(link.is_symlink() and link.lstat().st_uid == 0 and link.lstat().st_gid == 0 and link.resolve() == unit,
           "TOOL_UNIT_INVALID")


def stage_backup(source, manifest, parent, previous):
    """Stage a complete package, or recognize a previously completed exchange."""
    source, parent = Path(source), Path(parent)
    safe_parents(parent, parent)
    current = parent / "monkado_backup"
    staging = parent / ("monkado_backup.coordinator-" + manifest["archiveSha256"][:16])
    wanted = {Path(name).name: manifest["files"][name] for name in BACKUP_FILES}
    observed = backup_hashes(current)
    if observed == wanted:
        ensure(staging.is_dir() and not staging.is_symlink() and backup_hashes(staging) == previous,
               "BACKUP_CODE_INVALID")
        return current, staging
    ensure(observed == previous, "BACKUP_CODE_CHANGED")
    if not staging.exists() and not staging.is_symlink():
        staging.mkdir(mode=0o755)
        sync_directory(parent)
    safe_parents(staging, parent)
    for name in BACKUP_FILES:
        source_path = source / name
        ensure(content_digest(source_path) == manifest["files"][name], "TOOL_FILE_CHANGED")
        install_new(staging / Path(name).name, source_path.read_bytes(), 0o644)
    ensure(backup_hashes(staging) == wanted, "BACKUP_CODE_INVALID")
    return current, staging


def activate_backup(source, manifest, parent, previous):
    """Exchange complete packages once; identify an already completed exchange."""
    current, staging = stage_backup(source, manifest, parent, previous)
    wanted = {Path(name).name: manifest["files"][name] for name in BACKUP_FILES}
    if backup_hashes(current) != wanted:
        exchange(current, staging)
    ensure(backup_hashes(current) == wanted and backup_hashes(staging) == previous, "BACKUP_CODE_INVALID")
