"""Materialize approved native artifacts only after bounded HTTPS and digest verification."""

import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import shutil
import ssl
import stat
import tempfile
from time import monotonic
from urllib.parse import urljoin, urlsplit

from release_manifest import validate

HOSTS = {"github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"}
COMPONENTS = ("api", "worker", "migrate")


def require(condition):
    """Refuse untrusted material with a fixed technical diagnostic."""
    if not condition:
        raise ValueError("NATIVE_ARTIFACT_INVALID")


def location(value):
    """Restrict every provider redirect to known HTTPS GitHub release hosts."""
    require(isinstance(value, str) and len(value) <= 16384 and not any(ord(character) < 33 for character in value))
    parsed = urlsplit(value)
    require(parsed.scheme == "https" and parsed.hostname in HOSTS and parsed.port in (None, 443)
            and parsed.username is None and parsed.password is None and not parsed.fragment)
    return parsed


def download(url, destination, maximum, connection=http.client.HTTPSConnection, clock=monotonic):
    """Stream one bounded artifact without proxies, shell commands or diagnostic leakage."""
    deadline = clock() + 120
    context = ssl.create_default_context()
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    for redirect in range(4):
        parsed = location(url)
        budget = deadline - clock()
        require(budget > 0)
        client = connection(parsed.hostname, timeout=min(15, budget), context=context)
        try:
            target = parsed.path or "/"
            if parsed.query:
                target += "?" + parsed.query
            client.request("GET", target, headers={"User-Agent": "MonKado-native-release"})
            response = client.getresponse()
            if response.status in (301, 302, 303, 307, 308):
                following = response.getheader("Location")
                require(bool(following) and redirect < 3)
                url = urljoin(url, following)
                continue
            require(response.status == 200)
            size = response.getheader("Content-Length")
            require(size is None or re.fullmatch(r"[0-9]{1,9}", size) is not None and int(size) <= maximum)
            total = 0
            with destination.open("xb") as stream:
                os.chmod(destination, 0o600)
                while True:
                    require(clock() < deadline)
                    body = response.read(min(65536, maximum - total + 1))
                    if not body:
                        break
                    total += len(body)
                    require(total <= maximum)
                    stream.write(body)
                stream.flush()
                os.fsync(stream.fileno())
            return
        finally:
            client.close()
    raise ValueError("NATIVE_ARTIFACT_INVALID")


def identity(release):
    """Resolve one immutable publication directory, including rebuilds of the same source revision."""
    require(release.get("schemaVersion") == 3)
    validate(release, release["configurationHash"])
    return f'backend-{release["revision"]}-{release["publicationId"]}'


def verify_file(path, artifact):
    """Require root-owned immutable executable bytes matching the protected publication."""
    metadata = path.lstat()
    require(stat.S_ISREG(metadata.st_mode) and metadata.st_uid == 0 and metadata.st_mode & 0o022 == 0
            and metadata.st_size == artifact["size"])
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while body := stream.read(65536):
            digest.update(body)
    require(digest.hexdigest() == artifact["sha256"])


def verify_directory(directory, release):
    """Verify every retained artifact before reuse, rather than trusting an existing path."""
    metadata = directory.lstat()
    require(stat.S_ISDIR(metadata.st_mode) and metadata.st_uid == 0 and metadata.st_mode & 0o022 == 0)
    require({path.name for path in directory.iterdir()} == set(COMPONENTS))
    for component in COMPONENTS:
        verify_file(directory / component, release["binaries"][component])


def sync_directory(directory):
    """Make published directory entries durable before process replacement."""
    descriptor = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def materialize(root, release, transport=download):
    """Download into a private staging directory and publish only a fully verified immutable set."""
    name = identity(release)
    root.mkdir(mode=0o755, parents=True, exist_ok=True)
    metadata = root.lstat()
    require(stat.S_ISDIR(metadata.st_mode) and metadata.st_uid == 0 and metadata.st_mode & 0o022 == 0)
    destination = root / name
    if destination.exists() or destination.is_symlink():
        verify_directory(destination, release)
        return destination
    staging = Path(tempfile.mkdtemp(prefix=".native-release-", dir=root))
    try:
        for component in COMPONENTS:
            artifact = release["binaries"][component]
            path = staging / component
            transport(artifact["url"], path, artifact["size"])
            verify_file(path, artifact)
            path.chmod(0o755)
        verify_directory(staging, release)
        sync_directory(staging)
        staging.chmod(0o755)
        staging.rename(destination)
        sync_directory(root)
        return destination
    finally:
        # This exact temporary directory is created above under the verified installation root.
        if staging.exists():
            require(staging.resolve().parent == root.resolve() and not staging.is_symlink())
            shutil.rmtree(staging)


def inspect(directory, release, runner):
    """Execute metadata commands only after all downloaded bytes have been verified."""
    verify_directory(directory, release)
    for component in COMPONENTS:
        body = runner([str(directory / component), "--version"], maximum=1024)
        metadata = json.loads(body)
        require(isinstance(metadata, dict) and set(metadata) == {"schemaVersion", "revision", "os", "architecture"})
        require(type(metadata["schemaVersion"]) is int and metadata["schemaVersion"] == 1
                and metadata["revision"] == release["revision"] and metadata["os"] == "linux" and metadata["architecture"] == "amd64")
    catalog = json.loads(runner([str(directory / "migrate"), "--catalog"], maximum=32768))
    require(catalog == release["migrationCatalog"])
