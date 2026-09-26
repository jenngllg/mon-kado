"""Root-installed publication orchestration; no Docker, credentials or application mutations."""

from contextlib import contextmanager
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import ssl
import tempfile
import urllib.parse
import urllib.request

import frontend_contract as contract
import frontend_state as state_policy
from frontend_probe import probe

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

    def __init__(self, root, deployment, configuration_hash, transport=download, health=probe, clock=state_policy.now):
        self.root = root
        self.releases = root / "releases"
        self.backend_state = deployment
        self.configuration_hash = configuration_hash
        self.transport = transport
        self.health = health
        self.clock = clock
        self.journal = root / "transition.json"
        self.status_file = root / "status.json"

    def load(self):
        """Conservatively read legacy evidence without trusting its healthy label."""
        return state_policy.read(self.status_file, current_revision(self.releases))

    def save(self, state, **changes):
        """Commit a validated operational state before the next externally visible action."""
        state.update(changes, updatedAt=self.clock())
        atomic_json(self.status_file, state_policy.validate(state))

    def approved(self, staging, backend):
        """Resolve exactly the protected publication channel, without accepting alternate hosts."""
        pointer = staging / "release.json"
        self.transport(POINTER, pointer, 65536)
        return contract.from_release(json.loads(pointer.read_text(encoding="utf-8")), self.configuration_hash, backend)

    def manifest(self, revision, backend):
        """Never use a retained rollback artifact against a different active backend."""
        contract.require(contract.matches(contract.SHA, revision))
        path = self.root / "manifests" / (revision + ".json")
        contract.require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 4096)
        value = contract.validate(json.loads(path.read_text(encoding="utf-8")), self.configuration_hash, backend)
        contract.require(value["revision"] == revision)
        return value

    def materialize(self, manifest, staging):
        """Verify cached or downloaded archive bytes before trusting any installed directory."""
        revision = manifest["revision"]
        archives = self.root / "archives"
        archives.mkdir(mode=0o700, exist_ok=True)
        cached = archives / (revision + ".tar.gz")
        archive = cached
        if not cached.exists():
            contract.require(not cached.is_symlink())
            archive = staging / "frontend.tar.gz"
            self.transport(contract.archive_url(manifest), archive, contract.MAX_COMPRESSED)
        contract.require(archive.is_file() and not archive.is_symlink())
        extracted = staging / "extracted"
        contract.extract(archive, extracted, manifest)
        if archive != cached:
            archive.replace(cached)
            cached.chmod(0o600)
            sync_directory(archives)
        return extracted

    def verify(self, revision, backend):
        """Check local immutable contents and the actual HTTPS response, including on no-op runs."""
        manifest = self.manifest(revision, backend)
        with tempfile.TemporaryDirectory(prefix="verify-", dir=self.root) as temporary:
            extracted = self.materialize(manifest, Path(temporary))
            target = self.releases / revision
            contract.require(fingerprints(target) == fingerprints(extracted))
            self.health(revision, target, manifest.get("googleEnabled", False))
        return manifest

    def history(self):
        """Read only bounded canonical successful-release identifiers, never linked metadata."""
        history_path = self.root / "history.json"
        history = []
        contract.require(not history_path.is_symlink())
        if history_path.exists():
            contract.require(history_path.is_file() and history_path.stat().st_size <= 4096)
            history = json.loads(history_path.read_text(encoding="utf-8"))
            contract.require(isinstance(history, list) and len(history) <= 3)
            contract.require(all(contract.matches(contract.SHA, item) for item in history))
        return history

    def complete(self, manifest, backend, previous):
        """Record actual successful activation order, excluding failed staging attempts."""
        history = self.history()
        retained = list(dict.fromkeys(item for item in [manifest["revision"], previous, *history] if item is not None))[:3]
        atomic_json(self.root / "history.json", retained)
        return retained

    def finish(self, state, manifest, backend, previous, paused=False):
        """Commit success before journal removal and prune only after recovery is unnecessary."""
        retained = self.complete(manifest, backend, previous)
        self.save(state, phase="healthy", revision=manifest["revision"], lastVerifiedRevision=manifest["revision"],
                  candidateRevision=manifest["revision"], error=None, paused=paused, resumeDigest=None,
                  checks={"files": True, "https": True})
        self.journal.unlink(missing_ok=True)
        sync_directory(self.root)
        prune(self.releases, retained)
        for revision in list((self.root / "archives").iterdir()):
            if revision.name.endswith(".tar.gz") and contract.matches(contract.SHA, revision.name[:-7]) and revision.name[:-7] not in retained:
                contract.require(revision.is_file() and not revision.is_symlink())
                revision.unlink()

    def recover(self):
        """Recover once, verifying the restored version and keeping any uncertainty durable."""
        if not self.journal.exists():
            return
        state = self.load()
        committed_revision = state["revision"] if state["phase"] == "healthy" else None
        self.save(state, phase="recovering", paused=True, resumeDigest=None, error="ROLLOUT_INTERRUPTED")
        try:
            contract.require(not self.journal.is_symlink() and self.journal.stat().st_size <= 4096)
            transition = json.loads(self.journal.read_text(encoding="utf-8"))
            contract.require(set(transition) == {"previous", "revision"})
            contract.require(contract.matches(contract.SHA, transition["revision"]))
            previous = transition["previous"]
            contract.require(previous is None or contract.matches(contract.SHA, previous))
            self.save(state, candidateRevision=transition["revision"])
            backend = backend_revision(self.backend_state)
            if committed_revision == transition["revision"]:
                contract.require(current_revision(self.releases) == transition["revision"])
                manifest = self.verify(transition["revision"], backend)
                self.finish(state, manifest, backend, previous, paused=True)
                return
            if previous is not None:
                self.manifest(previous, backend)
            switch(self.releases, previous)
            if previous is not None:
                self.verify(previous, backend)
            self.save(state, phase="rolledBack" if previous else "unavailable", revision=previous,
                      lastVerifiedRevision=previous, checks={"files": True, "https": True} if previous else {},
                      error="PUBLICATION_FAILED" if previous else "NO_PREVIOUS_RELEASE")
            self.journal.unlink()
            sync_directory(self.root)
        except Exception:
            self.save(state, phase="recoveryRequired", error="ROLLBACK_FAILED", checks={})
            raise

    def pause(self):
        """Persist a manual suspension independently of timer enablement or process lifetime."""
        with locks(self.backend_state):
            state = self.load()
            self.save(state, paused=True, resumeDigest=None)
        return "paused"

    def resume(self, revision):
        """Authorize exactly one presently approved manifest after checking the active installation."""
        contract.require(contract.matches(contract.SHA, revision))
        with locks(self.backend_state):
            contract.require(not self.journal.exists())
            state = self.load()
            contract.require(state["phase"] not in {"activating", "recovering", "recoveryRequired"})
            backend = backend_revision(self.backend_state)
            active = current_revision(self.releases)
            if active is not None:
                self.verify(active, backend)
            with tempfile.TemporaryDirectory(prefix="resume-", dir=self.root) as temporary:
                staging = Path(temporary)
                manifest = self.approved(staging, backend)
                contract.require(manifest["revision"] == revision)
                self.materialize(manifest, staging)
            self.save(state, phase="failed" if state["error"] else "legacy", paused=False,
                      resumeDigest=state_policy.resume_digest(manifest))
        return "resumed"

    def rollback(self, revision):
        """Freeze automatic publication before validating any manually chosen rollback target."""
        contract.require(contract.matches(contract.SHA, revision))
        with locks(self.backend_state):
            state = self.load()
            self.save(state, paused=True, resumeDigest=None)
            contract.require(not self.journal.exists())
            backend = backend_revision(self.backend_state)
            manifest = self.manifest(revision, backend)
            contract.require(revision in self.history())
            return self.attempt(state, manifest, backend, stay_paused=True)

    def deploy(self, approved=None, retry=False):
        """A timer cannot resume failed releases, even with the historical retry switch."""
        with locks(self.backend_state):
            state = self.load()
            if self.journal.exists():
                if state["phase"] == "recoveryRequired":
                    return "paused"
                self.recover()
                return "recovered"
            if state["phase"] in {"preparing", "activating", "recovering"}:
                self.save(state, phase="failed", paused=True, resumeDigest=None, error="ROLLOUT_INTERRUPTED")
            if state["paused"]:
                return "paused"
            try:
                backend = backend_revision(self.backend_state)
                with tempfile.TemporaryDirectory(prefix="resolve-", dir=self.root) as temporary:
                    manifest = self.approved(Path(temporary), backend) if approved is None else contract.validate(approved, self.configuration_hash, backend)
                contract.require(state["resumeDigest"] is None or state["resumeDigest"] == state_policy.resume_digest(manifest))
                return self.attempt(state, manifest, backend)
            except Exception:
                state = self.load()
                if not self.journal.exists() and state["phase"] not in {"rolledBack", "unavailable"}:
                    self.save(state, phase="failed", paused=True, resumeDigest=None, error="PUBLICATION_FAILED", checks={})
                raise

    def attempt(self, state, manifest, backend, stay_paused=False):
        """Perform one immutable switch, retaining a verified fallback and durable recovery intent."""
        revision = manifest["revision"]
        previous = current_revision(self.releases)
        self.save(state, phase="preparing", candidateRevision=revision, resumeDigest=None, checks={})
        try:
            with tempfile.TemporaryDirectory(prefix="attempt-", dir=self.root) as temporary:
                contract.require(shutil.disk_usage(self.root).free >= contract.MAX_COMPRESSED + contract.MAX_EXTRACTED + DISK_RESERVE)
                if previous is not None:
                    # A deliberate rollback must not depend on the broken site's HTTP health.
                    old = self.manifest(previous, backend) if stay_paused and previous != revision else self.verify(previous, backend)
                    contract.require(previous != revision or old == manifest)
                if previous == revision:
                    self.finish(state, manifest, backend, previous, stay_paused)
                    return "unchanged"
                extracted = self.materialize(manifest, Path(temporary))
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
                self.save(state, phase="activating")
                switch(self.releases, revision)
                self.health(revision, target, manifest.get("googleEnabled", False))
                self.finish(state, manifest, backend, previous, stay_paused)
                return "installed"
        except Exception:
            if self.journal.exists():
                self.recover()
            else:
                self.save(state, phase="failed", paused=True, resumeDigest=None, error="PUBLICATION_FAILED", checks={})
            raise
