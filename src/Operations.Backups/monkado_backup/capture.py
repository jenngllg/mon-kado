"""Bounded-memory manifests and file copying; no external providers."""

import hashlib
import json
import os
import re
import shutil
import stat
import tempfile
from pathlib import Path

from .policy import BackupError, FILES, LEGACY_FILES, PREVIOUS_FILES, DEPLOYMENT_FILES, LEGACY_CURRENT_FILES, LEGACY_NATIVE_FILES, HASH, release_metadata, timestamp

IMAGE_NAME = re.compile(r"(?P<a>[0-9a-f]{2})/(?P<b>[0-9a-f]{2})/(?P=a)(?P=b)[0-9a-f]{28}\.(webp|pending)")
NATIVE_IMAGE_NAME = re.compile(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\.(webp|pending)")
# The repository also contains revocation records, not only key-{guid}.xml.
KEY_NAME = re.compile(r"[A-Za-z0-9_-]+\.xml")
MANIFEST_NAME = "manifest.json"
RELEASE_PATH = "configuration/current.env"


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
        if relative == MANIFEST_NAME:
            continue
        result[relative] = {"sha256": digest(path), "bytes": path.stat().st_size}
    return result


def validate_shape(paths, schema_version=4):
    """Prevent a restored archive from injecting extra configuration or secrets."""
    required = {"postgres.dump", "configuration/production.env", RELEASE_PATH}
    configuration = {1: LEGACY_FILES, 2: PREVIOUS_FILES, 3: DEPLOYMENT_FILES, 4: LEGACY_CURRENT_FILES,
                     5: LEGACY_NATIVE_FILES, 6: FILES}[schema_version]
    required.update("configuration/" + name for name in configuration)
    if schema_version >= 5:
        required.update({"configuration/caddy.env", "configuration/active.env", "configuration/deployment-status.json",
                         "binaries/api", "binaries/worker", "binaries/migrate"})
    if schema_version >= 6:
        required.add("configuration/coordinated-manifest.json")
    if not required.issubset(paths):
        raise BackupError("INCOMPLETE_CAPTURE")
    for name in paths:
        if name in required:
            continue
        if schema_version >= 3 and name in {"configuration/deployment-smoke.json", "configuration/deployment-status.json"}:
            continue
        parent, _, filename = name.rpartition("/")
        pattern = NATIVE_IMAGE_NAME if schema_version >= 5 else IMAGE_NAME
        if name.startswith("images/") and pattern.fullmatch(name[len("images/"):]):
            continue
        if schema_version < 5 and parent == "keys" and KEY_NAME.fullmatch(filename):
            continue
        raise BackupError("UNEXPECTED_CAPTURE_FILE")
    if schema_version < 5 and not any(name.startswith("keys/") for name in paths):
        raise BackupError("MISSING_DATA_PROTECTION_KEYS")


def create_manifest(root, created_at, postgres_image, postgres_version, caddy_image):
    """Bind the coherent capture to its runtime and cryptographic file inventory."""
    timestamp(created_at)
    if not re.fullmatch(r"postgres@sha256:" + HASH, postgres_image):
        raise BackupError("INVALID_POSTGRES_IMAGE")
    if not re.fullmatch(r"18\.\d+", postgres_version, flags=re.ASCII):
        raise BackupError("UNSUPPORTED_POSTGRES_VERSION")
    if not isinstance(caddy_image, str) or not re.fullmatch(r"caddy@sha256:" + HASH, caddy_image):
        raise BackupError("INVALID_CADDY_IMAGE")
    inventory = entries(root)
    validate_shape(inventory)
    value = {
        "schemaVersion": 4,
        "createdAt": created_at,
        "postgresImage": postgres_image,
        "postgresVersion": postgres_version,
        "caddyImage": caddy_image,
        "release": release_metadata((root / RELEASE_PATH).read_text()),
        "files": inventory,
    }
    atomic_json(root / MANIFEST_NAME, value)
    return value


def verify_manifest(root):
    """Verify every byte and reject unexpected files before a restore can run."""
    regular(root / MANIFEST_NAME)
    value = json.loads((root / MANIFEST_NAME).read_text())
    if not isinstance(value, dict) or type(value.get("schemaVersion")) is not int or value["schemaVersion"] not in (1, 2, 3, 4, 5, 6):
        raise BackupError("INVALID_MANIFEST")
    if value["schemaVersion"] >= 5:
        return verify_native_manifest(root, value)
    fields = {"schemaVersion", "createdAt", "postgresImage", "postgresVersion", "release", "files"}
    if value["schemaVersion"] >= 2:
        fields.add("caddyImage")
    if set(value) != fields:
        raise BackupError("INVALID_MANIFEST")
    if value["schemaVersion"] >= 2 and (not isinstance(value["caddyImage"], str) or
                                       not re.fullmatch(r"caddy@sha256:" + HASH, value["caddyImage"])):
        raise BackupError("INVALID_CADDY_IMAGE")
    timestamp(value["createdAt"])
    if not isinstance(value["postgresImage"], str) or not re.fullmatch(r"postgres@sha256:" + HASH, value["postgresImage"]):
        raise BackupError("INVALID_POSTGRES_IMAGE")
    if not isinstance(value["postgresVersion"], str) or not re.fullmatch(r"18\.\d+", value["postgresVersion"], flags=re.ASCII):
        raise BackupError("UNSUPPORTED_POSTGRES_VERSION")
    inventory = entries(root)
    validate_shape(inventory, value["schemaVersion"])
    if inventory != value["files"]:
        raise BackupError("CAPTURE_INTEGRITY_FAILED")
    release = release_metadata((root / RELEASE_PATH).read_text())
    if value["release"] != release:
        raise BackupError("RELEASE_MISMATCH")
    return value


def native_release(root, inventory):
    """Bind captured executables and SQL metadata to the retained approved native publication."""
    from release_manifest import configuration_hash, validate
    from deploy_policy import dotenv, validate_state
    from native_configuration import validate_database_environment, validate_runtime_keys

    state_path = root / "configuration/deployment-status.json"
    if state_path.stat().st_size > 131072:
        raise BackupError("INVALID_DEPLOYMENT_STATE")
    state = validate_state(json.loads(state_path.read_bytes()))
    if state["phase"] not in ("succeeded", "acknowledged", "rolledBack", "rejected") or state["current"] is None:
        raise BackupError("DEPLOYMENT_INCOMPLETE")
    approved = validate(state["current"], configuration_hash(root / "configuration"))
    if approved["schemaVersion"] != 3:
        raise BackupError("NATIVE_PUBLICATION_REQUIRED")
    pointer = dotenv(approved).encode()
    if any((root / "configuration" / name).read_bytes() != pointer for name in ("current.env", "active.env")):
        raise BackupError("RELEASE_MISMATCH")
    for component in ("api", "worker", "migrate"):
        artifact = approved["binaries"][component]
        if inventory["binaries/" + component] != {"sha256": artifact["sha256"], "bytes": artifact["size"]}:
            raise BackupError("BINARY_INTEGRITY_FAILED")
    environment = root / "configuration/production.env"
    if environment.stat().st_size > 131072:
        raise BackupError("INVALID_PRIVATE_CONFIGURATION")
    raw = environment.read_bytes()
    validate_database_environment(raw)
    validate_runtime_keys(raw)
    return release_metadata(pointer.decode())


def create_native_manifest(root, created_at, postgres_digest, postgres_version, caddy_digest):
    """Record a coherent native capture including stable private keys and all executable bytes."""
    value = {"schemaVersion": 6, "createdAt": created_at, "postgresSHA256": postgres_digest,
             "postgresVersion": postgres_version, "caddySHA256": caddy_digest, "files": entries(root)}
    validate_shape(value["files"], 6)
    value["release"] = native_release(root, value["files"])
    verify_coordinator(root, value["files"])
    validate_native_metadata(value)
    atomic_json(root / MANIFEST_NAME, value)
    return value


def validate_native_metadata(value):
    """Reject unknown manifest fields, runtime hashes and unsupported native PostgreSQL versions."""
    if set(value) != {"schemaVersion", "createdAt", "postgresSHA256", "postgresVersion", "caddySHA256", "release", "files"}:
        raise BackupError("INVALID_MANIFEST")
    timestamp(value["createdAt"])
    for key in ("postgresSHA256", "caddySHA256"):
        if not isinstance(value[key], str) or not re.fullmatch(HASH, value[key]):
            raise BackupError("INVALID_RUNTIME_DIGEST")
    if not isinstance(value["postgresVersion"], str) or not re.fullmatch(r"18\.[0-9]+", value["postgresVersion"]):
        raise BackupError("UNSUPPORTED_POSTGRES_VERSION")


def verify_native_manifest(root, value):
    """Verify native captured bytes and approval metadata before any restoration side effect."""
    validate_native_metadata(value)
    inventory = entries(root)
    validate_shape(inventory, value["schemaVersion"])
    if inventory != value["files"]:
        raise BackupError("CAPTURE_INTEGRITY_FAILED")
    if native_release(root, inventory) != value["release"]:
        raise BackupError("RELEASE_MISMATCH")
    if value["schemaVersion"] >= 6:
        verify_coordinator(root, inventory)
    return value


def verify_coordinator(root, inventory):
    """Bind the captured coordination and backup code to its reviewed manifest."""
    from coordinated_integrity import FILES as tool_files, validate
    from release_manifest import configuration_hash

    path = root / "configuration/coordinated-manifest.json"
    if path.stat().st_size > 65536:
        raise BackupError("COORDINATOR_MANIFEST_INVALID")
    manifest = validate(json.loads(path.read_bytes()), configuration_hash(root / "configuration"))
    for name in tool_files:
        if inventory["configuration/" + name]["sha256"] != manifest["files"][name]:
            raise BackupError("COORDINATOR_INTEGRITY_FAILED")


def copy_deployment_private(settings, state, configuration):
    """Include only explicit deployment credentials/state, inside the encrypted capture."""
    for source, name in ((settings / "deployment-smoke.json", "deployment-smoke.json"),
                         (settings / "coordinated-manifest.json", "coordinated-manifest.json"),
                         (state / "status.json", "deployment-status.json")):
        if not source.exists() and not source.is_symlink():
            continue
        regular(source)
        metadata = source.stat()
        if metadata.st_uid != 0 or metadata.st_gid != 0 or stat.S_IMODE(metadata.st_mode) != 0o600:
            raise BackupError("UNSAFE_DEPLOYMENT_FILE")
        target = configuration / name
        shutil.copyfile(source, target)
        target.chmod(0o600)
