"""Bounded private operational storage; no raw logs or credentials in measurement history."""

from datetime import timedelta
import json
import os
import re
import stat

from monitor_policy import require, timestamp

MAX_JSON_BYTES = 65536
HISTORY_BYTES = 32 * 1024 * 1024
JSON_SUFFIX = ".json"


def read_json(path, private=False):
    """Read only a bounded regular file; credential files must be root-only."""
    metadata = path.lstat()
    require(stat.S_ISREG(metadata.st_mode) and metadata.st_size <= MAX_JSON_BYTES)
    if private:
        require(metadata.st_uid == 0 and stat.S_IMODE(metadata.st_mode) == 0o600)
    with path.open("rb") as source:
        data = source.read(MAX_JSON_BYTES + 1)
    require(len(data) <= MAX_JSON_BYTES)
    return json.loads(data)


def atomic_json(path, value):
    """Replace private state durably without exposing a partially written snapshot."""
    require(not path.is_symlink())
    data = json.dumps(value, allow_nan=False, separators=(",", ":"), sort_keys=True).encode()
    require(len(data) <= MAX_JSON_BYTES)
    temporary = path.with_suffix(".new")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, "wb") as output:
        os.fchmod(output.fileno(), 0o600)
        output.write(data)
        output.flush()
        os.fsync(output.fileno())
    temporary.replace(path)
    descriptor = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def append_history(directory, now, measurement, maximum_bytes=HISTORY_BYTES):
    """Retain seven days under a hard total-size limit, deleting only owned sample files."""
    directory.mkdir(mode=0o700, exist_ok=True)
    require(not directory.is_symlink())
    path = directory / (now.strftime("%Y%m%dT%H%MZ") + JSON_SUFFIX)
    size_needed = len(json.dumps(measurement, allow_nan=False, separators=(",", ":"), sort_keys=True).encode())
    require(size_needed <= min(MAX_JSON_BYTES, maximum_bytes))
    files = []
    for candidate in directory.iterdir():
        if candidate.suffix not in (JSON_SUFFIX, ".new"):
            continue
        require(re.fullmatch(r"\d{8}T\d{4}Z\.(json|new)", candidate.name, flags=re.ASCII) is not None)
        metadata = candidate.lstat()
        require(stat.S_ISREG(metadata.st_mode))
        if candidate.suffix == ".new":
            # The caller holds the monitor lock; these can only be interrupted writes.
            candidate.unlink()
            continue
        files.append((candidate, metadata.st_size))
    files.sort(key=lambda item: item[0].name)
    total = sum(size for _, size in files)
    cutoff = (now - timedelta(days=7)).strftime("%Y%m%dT%H%MZ.json")
    for candidate, size in files:
        if candidate.name < cutoff or total + size_needed > maximum_bytes:
            candidate.unlink()
            total -= size
    atomic_json(path, measurement)


def recent_snapshots(directory, now, service="api"):
    """Read only the last twelve minutes needed by HTTP alert windows."""
    if not directory.exists():
        return []
    result = []
    for offset in range(1, 13):
        path = directory / ((now - timedelta(minutes=offset)).strftime("%Y%m%dT%H%MZ") + JSON_SUFFIX)
        if path.exists():
            snapshot = read_json(path).get(service)
            if snapshot is not None:
                timestamp(snapshot["createdAt"])
                result.append(snapshot)
    return result
