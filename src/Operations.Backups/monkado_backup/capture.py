"""Bounded-memory manifests and file copying; no external providers."""

import hashlib
import json
import os
import re
import shutil
import stat
import tempfile
from pathlib import Path

from .policy import BackupError, FILES, LEGACY_FILES, HASH, release_metadata, timestamp

IMAGE_NAME = re.compile(r"(?P<a>[0-9a-f]{2})/(?P<b>[0-9a-f]{2})/(?P=a)(?P=b)[0-9a-f]{28}\.(webp|pending)")
# The repository also contains revocation records, not only key-{guid}.xml.
KEY_NAME = re.compile(r"[A-Za-z0-9_-]+\.xml")


def regular(path):
    """Refuse links and special files before reading any captured content."""
    if not stat.S_ISREG(path.lstat().st_mode):
        raise BackupError("UNSAFE_FILE")


def digest(path):
    """Hash a file without loading images or dumps into memory."""
    regular(path)
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def atomic_json(path, value):
    """Publish a private JSON document only after its bytes are durable."""
    descriptor, name = tempfile.mkstemp(prefix=".state-", dir=path.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(value, stream, sort_keys=True)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        temporary.unlink(missing_ok=True)


def copy_files(source, target, pattern):
    """Copy recognized paths without following links or capturing temporary files."""
    target.mkdir(mode=0o700)
    for path in source.rglob("*"):
        relative = path.relative_to(source)
        if not pattern.fullmatch(relative.as_posix()):
            continue
        regular(path)
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        shutil.copyfile(path, destination)
        destination.chmod(0o600)


def entries(root):
    """Describe all captured bytes; reject links, including linked directories."""
    result = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise BackupError("UNSAFE_FILE")
        if path.is_dir():
            continue
        relative = path.relative_to(root).as_posix()
        if relative == "manifest.json":
            continue
        result[relative] = {"sha256": digest(path), "bytes": path.stat().st_size}
    return result


def validate_shape(paths, schema_version=2):
    """Prevent a restored archive from injecting extra configuration or secrets."""
    required = {"postgres.dump", "configuration/production.env", "configuration/current.env"}
    required.update("configuration/" + name for name in (LEGACY_FILES if schema_version == 1 else FILES))
    if not required.issubset(paths):
        raise BackupError("INCOMPLETE_CAPTURE")
    for name in paths:
        if name in required:
            continue
        parent, _, filename = name.rpartition("/")
        if name.startswith("images/") and IMAGE_NAME.fullmatch(name[len("images/"):]):
            continue
        if parent == "keys" and KEY_NAME.fullmatch(filename):
            continue
        raise BackupError("UNEXPECTED_CAPTURE_FILE")
    if not any(name.startswith("keys/") for name in paths):
        raise BackupError("MISSING_DATA_PROTECTION_KEYS")


def create_manifest(root, created_at, postgres_image, postgres_version, caddy_image):
    """Bind the coherent capture to its runtime and cryptographic file inventory."""
    timestamp(created_at)
    if not re.fullmatch(r"postgres@sha256:" + HASH, postgres_image):
        raise BackupError("INVALID_POSTGRES_IMAGE")
    if not re.fullmatch(r"18\.[0-9]+", postgres_version):
        raise BackupError("UNSUPPORTED_POSTGRES_VERSION")
    if not isinstance(caddy_image, str) or not re.fullmatch(r"caddy@sha256:" + HASH, caddy_image):
        raise BackupError("INVALID_CADDY_IMAGE")
    inventory = entries(root)
    validate_shape(inventory)
    value = {
        "schemaVersion": 2,
        "createdAt": created_at,
        "postgresImage": postgres_image,
        "postgresVersion": postgres_version,
        "caddyImage": caddy_image,
        "release": release_metadata((root / "configuration/current.env").read_text()),
        "files": inventory,
    }
    atomic_json(root / "manifest.json", value)
    return value


def verify_manifest(root):
    """Verify every byte and reject unexpected files before a restore can run."""
    regular(root / "manifest.json")
    value = json.loads((root / "manifest.json").read_text())
    if not isinstance(value, dict) or type(value.get("schemaVersion")) is not int or value["schemaVersion"] not in (1, 2):
        raise BackupError("INVALID_MANIFEST")
    fields = {"schemaVersion", "createdAt", "postgresImage", "postgresVersion", "release", "files"}
    if value["schemaVersion"] == 2:
        fields.add("caddyImage")
    if set(value) != fields:
        raise BackupError("INVALID_MANIFEST")
    if value["schemaVersion"] == 2 and (not isinstance(value["caddyImage"], str) or
                                       not re.fullmatch(r"caddy@sha256:" + HASH, value["caddyImage"])):
        raise BackupError("INVALID_CADDY_IMAGE")
    timestamp(value["createdAt"])
    if not isinstance(value["postgresImage"], str) or not re.fullmatch(r"postgres@sha256:" + HASH, value["postgresImage"]):
        raise BackupError("INVALID_POSTGRES_IMAGE")
    if not isinstance(value["postgresVersion"], str) or not re.fullmatch(r"18\.[0-9]+", value["postgresVersion"]):
        raise BackupError("UNSUPPORTED_POSTGRES_VERSION")
    inventory = entries(root)
    validate_shape(inventory, value["schemaVersion"])
    if inventory != value["files"]:
        raise BackupError("CAPTURE_INTEGRITY_FAILED")
    release = release_metadata((root / "configuration/current.env").read_text())
    if value["release"] != release:
        raise BackupError("RELEASE_MISMATCH")
    return value
