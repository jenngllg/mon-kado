"""Describe the exact migration sources shipped with a release, without a database."""

import hashlib
import json
import re
import sys
from pathlib import Path

HASH = r"[0-9a-f]{64}"
LEGACY_MIGRATION = r"(?a:\d{14}_\w+)"
NATIVE_MIGRATION = r"(?a:\d{3}_[a-z0-9_]+\.sql)"
MIGRATION = rf"(?:{LEGACY_MIGRATION}|{NATIVE_MIGRATION})"


def require(condition, code="INVALID_CATALOG"):
    """Reject untrusted metadata using a fixed, non-sensitive diagnostic."""
    if not condition:
        raise ValueError(code)


def canonical(value):
    """Produce stable bytes for a versioned contract."""
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")


def fingerprint(value):
    """Bind every catalog field to the publication and image."""
    return hashlib.sha256(canonical(value)).hexdigest()


def validate_catalog(value):
    """Accept only ordered, unique migration identities and source hashes."""
    require(isinstance(value, dict) and set(value) == {"schemaVersion", "migrations", "modelHash"})
    require(type(value["schemaVersion"]) is int and value["schemaVersion"] in (1, 2))
    require(isinstance(value["modelHash"], str) and re.fullmatch(HASH, value["modelHash"]) is not None)
    entries = value["migrations"]
    require(isinstance(entries, list) and 0 < len(entries) <= 200)
    identifiers = []
    for entry in entries:
        require(isinstance(entry, dict) and set(entry) == {"id", "sha256"})
        pattern = NATIVE_MIGRATION if value["schemaVersion"] == 2 else LEGACY_MIGRATION
        require(isinstance(entry["id"], str) and re.fullmatch(pattern, entry["id"]) is not None)
        require(isinstance(entry["sha256"], str) and re.fullmatch(HASH, entry["sha256"]) is not None)
        identifiers.append(entry["id"])
    require(identifiers == sorted(set(identifiers)))
    return value


def generate(directory):
    """Hash native embedded SQL; retain legacy catalog inspection for historical publications."""
    sources = sorted(directory.glob("*.sql"))
    if sources:
        migrations = []
        model = hashlib.sha256()
        for source in sources:
            body = source.read_bytes().replace(b"\r\n", b"\n")
            migrations.append({"id": source.name, "sha256": hashlib.sha256(body).hexdigest()})
            model.update(source.name.encode("ascii") + b"\0" + body + b"\0")
        return validate_catalog({"schemaVersion": 2, "migrations": migrations, "modelHash": model.hexdigest()})
    migrations = []
    for metadata in sorted(directory.glob("*.Designer.cs")):
        identifier = metadata.name.removesuffix(".Designer.cs")
        source = directory / (identifier + ".cs")
        body = metadata.read_bytes().replace(b"\r\n", b"\n")
        require(re.findall(rb'\[Migration\("([^"\r\n]+)"\)\]', body) == [identifier.encode()])
        digest = hashlib.sha256(source.read_bytes().replace(b"\r\n", b"\n") + b"\0" + body).hexdigest()
        migrations.append({"id": identifier, "sha256": digest})
    snapshot = (directory / "MonKadoDbContextModelSnapshot.cs").read_bytes().replace(b"\r\n", b"\n")
    return validate_catalog({"schemaVersion": 1, "migrations": migrations,
                             "modelHash": hashlib.sha256(snapshot).hexdigest()})


def identifiers(catalog):
    """Return the complete ordered history rather than only the latest migration."""
    return [entry["id"] for entry in validate_catalog(catalog)["migrations"]]


def main(arguments):
    """Emit only public migration metadata for the native build or publication job."""
    require(len(arguments) == 1, "INVALID_ARGUMENTS")
    print(canonical(generate(Path(arguments[0]))).decode("utf-8"))


if __name__ == "__main__":
    main(sys.argv[1:])
