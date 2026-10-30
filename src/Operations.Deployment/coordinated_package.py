"""Create and validate a bounded, fixed-inventory coordination tool archive."""

import gzip
import hashlib
import json
from pathlib import Path
import tarfile

from coordinated_integrity import FILES, MAXIMUM_FILE_BYTES, create, validate
from deploy_policy import ensure

MAXIMUM_ARCHIVE_BYTES = 32 * 1024 * 1024


def package(source, output, revision, configuration):
    """Create deterministic bytes and a manifest from a reviewed source checkout."""
    source, output = Path(source).absolute(), Path(output).absolute()
    ensure(not output.exists() and not output.is_symlink(), "TOOL_OUTPUT_EXISTS")
    output.mkdir(mode=0o700)
    archive = output / "coordinator.tar.gz"
    with archive.open("xb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", filename="", mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as package_file:
            for name in sorted(FILES):
                path = source / name
                metadata = package_file.gettarinfo(str(path), arcname=name)
                metadata.uid = metadata.gid = metadata.mtime = 0
                metadata.uname = metadata.gname = ""
                metadata.mode = 0o644
                metadata.pax_headers = {}
                with path.open("rb") as stream:
                    package_file.addfile(metadata, stream)
    ensure(archive.stat().st_size <= MAXIMUM_ARCHIVE_BYTES, "TOOL_ARCHIVE_TOO_LARGE")
    with archive.open("rb") as stream:
        archive_digest = hashlib.file_digest(stream, "sha256").hexdigest()
    manifest = create(source, revision, configuration, archive_digest)
    (output / "manifest.json").write_text(json.dumps(manifest, sort_keys=True, separators=(",", ":")), encoding="utf-8")
    return manifest


def extract(archive, destination, manifest, configuration):
    """Refuse links, extra paths, duplicates and changed bytes before use."""
    validate(manifest, configuration)
    archive, destination = Path(archive).absolute(), Path(destination).absolute()
    ensure(archive.is_file() and not archive.is_symlink() and archive.stat().st_size <= MAXIMUM_ARCHIVE_BYTES,
           "TOOL_ARCHIVE_INVALID")
    with archive.open("rb") as stream:
        ensure(hashlib.file_digest(stream, "sha256").hexdigest() == manifest["archiveSha256"], "TOOL_ARCHIVE_CHANGED")
    ensure(not destination.exists() and not destination.is_symlink(), "TOOL_OUTPUT_EXISTS")
    with tarfile.open(archive, "r|gz") as package_file:
        seen = set()
        destination.mkdir(mode=0o700)
        for entry in package_file:
            ensure(entry.name in FILES and entry.name not in seen and entry.isfile() and
                   0 <= entry.size <= MAXIMUM_FILE_BYTES, "TOOL_ARCHIVE_INVALID")
            seen.add(entry.name)
            target = destination / entry.name
            target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            source = package_file.extractfile(entry)
            ensure(source is not None, "TOOL_ARCHIVE_INVALID")
            checksum = hashlib.sha256()
            with source, target.open("xb") as output:
                while chunk := source.read(65536):
                    checksum.update(chunk)
                    output.write(chunk)
            target.chmod(0o644)
            ensure(checksum.hexdigest() == manifest["files"][entry.name], "TOOL_FILE_CHANGED")
        ensure(seen == set(FILES), "TOOL_ARCHIVE_INVALID")
