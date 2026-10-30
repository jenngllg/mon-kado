"""Stage a protected archive without importing any of its Python code."""

import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import tarfile


def stage(archive, manifest_file, manifest_sha256, destination):
    """Extract only bounded regular files from the independently pinned manifest."""
    archive, manifest_file, destination = map(Path, (archive, manifest_file, destination))
    if not re.fullmatch(r"[0-9a-f]{64}", manifest_sha256):
        raise ValueError("invalid manifest digest")
    raw = manifest_file.read_bytes()
    if len(raw) > 65536 or hashlib.sha256(raw).hexdigest() != manifest_sha256:
        raise ValueError("manifest digest mismatch")
    manifest = json.loads(raw)
    files = manifest.get("files")
    if not isinstance(files, dict) or not files or len(files) > 100:
        raise ValueError("invalid inventory")
    for name, digest in files.items():
        path = PurePosixPath(name)
        if (not isinstance(name, str) or path.is_absolute() or str(path) != name or
                any(part in (".", "..") for part in path.parts) or
                not (name.startswith("src/Operations.") or name.startswith("deployments/production/")) or
                not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest)):
            raise ValueError("invalid inventory")
    if archive.is_symlink() or not archive.is_file() or archive.stat().st_size > 32 * 1024 * 1024:
        raise ValueError("invalid archive")
    with archive.open("rb") as source:
        if hashlib.file_digest(source, "sha256").hexdigest() != manifest.get("archiveSha256"):
            raise ValueError("archive digest mismatch")
    if not destination.is_absolute() or destination.resolve() != destination:
        raise ValueError("invalid destination")
    destination.mkdir(mode=0o700)
    seen = set()
    with tarfile.open(archive, "r|gz") as bundle:
        for entry in bundle:
            if entry.name not in files or entry.name in seen or not entry.isfile() or entry.size > 2 * 1024 * 1024:
                raise ValueError("invalid archive entry")
            seen.add(entry.name)
            target = destination / entry.name
            target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            source = bundle.extractfile(entry)
            if source is None:
                raise ValueError("missing archive entry")
            with source, target.open("xb") as output:
                data = source.read(2 * 1024 * 1024 + 1)
                if len(data) != entry.size or hashlib.sha256(data).hexdigest() != files[entry.name]:
                    raise ValueError("archive entry mismatch")
                output.write(data)
                output.flush()
                os.fsync(output.fileno())
            target.chmod(0o644)
    if seen != set(files):
        raise ValueError("incomplete archive")
    with (destination / "approved-manifest.json").open("xb") as output:
        os.fchmod(output.fileno(), 0o600)
        output.write(raw)
        output.flush()
        os.fsync(output.fileno())


if __name__ == "__main__":
    import sys
    if os.geteuid() != 0 or len(sys.argv) != 5:
        raise SystemExit("root and four arguments required")
    stage(*sys.argv[1:])
