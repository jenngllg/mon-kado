"""Root-installed publication orchestration; no Docker, credentials or application mutations."""

from contextlib import contextmanager
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import ssl
import subprocess
import tempfile
import urllib.parse
import urllib.request

import frontend_contract as contract

POINTER = f"https://api.github.com/repos/{contract.REPOSITORY}/releases/tags/frontend-production"
ALLOWED_HOSTS = {"api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"}
DISK_RESERVE = 64 * 1024 * 1024


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    """Never follow a downloaded redirect outside GitHub's HTTPS artifact hosts."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        validate_url(newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def validate_url(url):
    """Accept HTTPS provider hosts only, without credentials or alternate ports."""
    parsed = urllib.parse.urlsplit(url)
    contract.require(parsed.scheme == "https" and parsed.hostname in ALLOWED_HOSTS)
    contract.require(parsed.username is None and parsed.password is None and parsed.port in (None, 443))


def download(url, destination, limit):
    """Stream bounded public data without printing provider URLs, headers or failures."""
    validate_url(url)
    context = ssl.create_default_context()
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.verify_mode = ssl.CERT_REQUIRED
    context.check_hostname = True
    opener = urllib.request.build_opener(SafeRedirect(), urllib.request.HTTPSHandler(context=context))
    request = urllib.request.Request(url, headers={"Accept": "application/octet-stream", "User-Agent": "MonKado-publication"})
    size = 0
    with opener.open(request, timeout=30) as response, destination.open("xb") as output:
        while chunk := response.read(65536):
            size += len(chunk)
            contract.require(size <= limit)
            output.write(chunk)


def atomic_json(path, value):
    """Persist bounded local state before making a live filesystem transition."""
    temporary = path.with_suffix(".new")
    with temporary.open("w", encoding="utf-8") as output:
        json.dump(value, output, sort_keys=True)
        output.flush()
        os.fsync(output.fileno())
    temporary.replace(path)
    path.chmod(0o600)
    sync_directory(path.parent)


def sync_directory(path):
    """Persist rename/unlink ordering on the Linux VPS filesystem."""
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


@contextmanager
def locks(deployment):
    """Use the backup guard's established lock order, without waiting during maintenance."""
    with (deployment / "backup-coordination.lock").open("a") as coordination:
        fcntl.flock(coordination, fcntl.LOCK_EX | fcntl.LOCK_NB)
        with (deployment / "deploy.lock").open("a") as rollout:
            fcntl.flock(rollout, fcntl.LOCK_EX | fcntl.LOCK_NB)
            yield


def current_revision(releases):
    """Read only a canonical local relative revision link, never follow arbitrary paths."""
    current = releases / "current"
    if not current.is_symlink():
        contract.require(not current.exists())
        return None
    value = os.readlink(current)
    contract.require(contract.matches(contract.SHA, value))
    contract.require((releases / value).is_dir() and not (releases / value).is_symlink())
    return value


def switch(releases, revision):
    """Atomically change the public directory without restarting Caddy or the backend."""
    current_revision(releases)
    current = releases / "current"
    if revision is None:
        current.unlink(missing_ok=True)
    else:
        contract.require(contract.matches(contract.SHA, revision))
        contract.require((releases / revision).is_dir() and not (releases / revision).is_symlink())
        temporary = releases / "current.new"
        temporary.unlink(missing_ok=True)
        temporary.symlink_to(revision, target_is_directory=True)
        temporary.replace(current)
    sync_directory(releases)


def backend_revision(deployment):
    """Read the non-secret confirmed backend revision, refusing interrupted rollouts."""
    contract.require(not (deployment / "in-progress.env").exists())
    data = (deployment / "current.env").read_text(encoding="utf-8")
    revisions = re.findall(r"^RELEASE_REVISION=([0-9a-f]{40})$", data, re.MULTILINE)
    contract.require(len(revisions) == 1)
    return revisions[0]


def probe(revision, assets):
    """Check this VPS through its public TLS names, with bounded outputs and no cookies."""
    checks = ["https://www.monkado.fr/release.json", "https://www.monkado.fr/", contract.API_ORIGIN + "/readiness"]
    checks.extend("https://www.monkado.fr/" + asset for asset in assets)
    for index, url in enumerate(checks):
        result = subprocess.run(
            ["curl", "--fail", "--silent", "--show-error", "--max-time", "15", "--max-filesize", "20971520",
             "--resolve", "www.monkado.fr:443:127.0.0.1", "--resolve", "api.monkado.fr:443:127.0.0.1", url],
            check=True, capture_output=True, timeout=20)
        if index == 0:
            contract.require(json.loads(result.stdout) == {"revision": revision, "apiOrigin": contract.API_ORIGIN, "googleEnabled": False})


def prune(releases, retained):
    """Retain active plus two previous releases; never enumerate unrelated paths for deletion."""
    candidates = [path for path in releases.iterdir()
                  if contract.matches(contract.SHA, path.name) and path.is_dir() and not path.is_symlink() and path.name not in retained]
    for path in candidates:
        shutil.rmtree(path)


def fingerprints(directory):
    """Compare an existing immutable build against a newly verified extraction."""
    contract.require(directory.is_dir() and not directory.is_symlink())
    result = {}
    for path in directory.rglob("*"):
        contract.require(not path.is_symlink())
        if path.is_dir():
            continue
        name = path.relative_to(directory).as_posix()
        contract.require(path.is_file() and contract.allowed_file(name))
        result[name] = contract.file_digest(path)
    return result


class Deployment:
    """Coordinate an immutable static release and recover interrupted pointer changes."""

    def __init__(self, root, deployment, configuration_hash, transport=download, health=probe):
        self.root = root
        self.releases = root / "releases"
        self.backend_state = deployment
        self.configuration_hash = configuration_hash
        self.transport = transport
        self.health = health
        self.journal = root / "transition.json"
        self.status_file = root / "status.json"

    def complete(self, manifest, backend, previous):
        """Record actual successful activation order, excluding failed staging attempts."""
        history_path = self.root / "history.json"
        history = []
        if history_path.exists():
            history = json.loads(history_path.read_text(encoding="utf-8"))
            contract.require(isinstance(history, list) and len(history) <= 3)
            contract.require(all(contract.matches(contract.SHA, item) for item in history))
        retained = list(dict.fromkeys(item for item in [manifest["revision"], previous, *history] if item is not None))[:3]
        atomic_json(history_path, retained)
        atomic_json(self.status_file, {"state": "healthy", "revision": manifest["revision"], "backendRevision": backend,
                                      "archiveSha256": manifest["archiveSha256"]})
        prune(self.releases, retained)

    def recover(self):
        """Restore the previous pointer before doing anything with another approved release."""
        if not self.journal.exists():
            return
        transition = json.loads(self.journal.read_text(encoding="utf-8"))
        contract.require(set(transition) == {"previous", "revision"})
        contract.require(contract.matches(contract.SHA, transition["revision"]))
        switch(self.releases, transition["previous"])
        atomic_json(self.status_file, {"state": "failed", "failedRevision": transition["revision"], "error": "ROLLOUT_INTERRUPTED"})
        self.journal.unlink()
        sync_directory(self.root)

    def deploy(self, approved=None, retry=False):
        """Install only after complete capture verification; restore the old site on failed smoke checks."""
        with locks(self.backend_state):
            self.recover()
            backend = backend_revision(self.backend_state)
            with tempfile.TemporaryDirectory(prefix="attempt-", dir=self.root) as temporary:
                staging = Path(temporary)
                if approved is None:
                    pointer = staging / "release.json"
                    self.transport(POINTER, pointer, 65536)
                    manifest = contract.from_release(json.loads(pointer.read_text(encoding="utf-8")), self.configuration_hash, backend)
                else:
                    manifest = contract.validate(approved, self.configuration_hash, backend)
                revision = manifest["revision"]
                previous = current_revision(self.releases)
                if previous == revision:
                    local_manifest = json.loads((self.root / "manifests" / (revision + ".json")).read_text(encoding="utf-8"))
                    contract.require(local_manifest == manifest)
                    self.complete(manifest, backend, previous)
                    return "unchanged"
                if self.status_file.exists():
                    status = json.loads(self.status_file.read_text(encoding="utf-8"))
                    contract.require(retry or status.get("failedRevision") != revision)
                contract.require(shutil.disk_usage(self.root).free >= contract.MAX_COMPRESSED + contract.MAX_EXTRACTED + DISK_RESERVE)
                archive = staging / "frontend.tar.gz"
                self.transport(contract.archive_url(manifest), archive, contract.MAX_COMPRESSED)
                extracted = staging / "extracted"
                contract.extract(archive, extracted, manifest)
                target = self.releases / revision
                if target.exists() or target.is_symlink():
                    contract.require(fingerprints(target) == fingerprints(extracted))
                else:
                    extracted.replace(target)
                sync_directory(self.releases)
                manifests = self.root / "manifests"
                manifests.mkdir(mode=0o700, exist_ok=True)
                atomic_json(manifests / (revision + ".json"), manifest)
                atomic_json(self.journal, {"previous": previous, "revision": revision})
                try:
                    switch(self.releases, revision)
                    assets = sorted(path.relative_to(target).as_posix() for path in (target / "assets").glob("*")
                                    if path.suffix in {".js", ".css"})
                    self.health(revision, assets)
                except Exception:
                    self.recover()
                    raise
                self.journal.unlink()
                sync_directory(self.root)
                self.complete(manifest, backend, previous)
                return "installed"
