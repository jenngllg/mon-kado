"""Stage, activate and restore strict frontend manifests within a paired rollout."""

import json
from pathlib import Path
import shutil
import tempfile

import frontend_contract as contract
import frontend_runtime as hosting
from deploy_policy import ensure


class CoordinatedFrontend:
    """Use the installed publisher's manifest, archive and HTTPS checks unchanged.

    The caller holds both deployment locks. The previous frontend is verified
    against its own backend before that backend changes; the candidate is
    verified against its own backend before public traffic is reopened.
    """

    def __init__(self, publisher, gate):
        self.publisher = publisher
        self.gate = gate

    def _current(self, pair):
        revision = pair["frontend"]["revision"]
        ensure(hosting.current_revision(self.publisher.releases) == revision, "FRONTEND_CHANGED")
        ensure(hosting.backend_revision(self.publisher.backend_state) == pair["backend"]["revision"], "BACKEND_CHANGED")

    def verify_retained(self, pair):
        """Check original binding and bytes without making assumptions about a live URL."""
        revision, backend = pair["frontend"]["revision"], pair["backend"]["revision"]
        manifest = self.publisher.manifest(revision, backend)
        ensure(manifest == pair["frontend"], "FRONTEND_MANIFEST_CHANGED")
        with tempfile.TemporaryDirectory(prefix="pair-verify-", dir=self.publisher.root) as temporary:
            extracted = self.publisher.materialize(manifest, Path(temporary))
            ensure(hosting.fingerprints(self.publisher.releases / revision) == hosting.fingerprints(extracted),
                   "FRONTEND_FILES_CHANGED")
        return manifest

    def _approved(self, pair, staging):
        manifest = self.publisher.approved(staging, pair["backend"]["revision"])
        ensure(manifest == pair["frontend"], "FRONTEND_APPROVAL_CHANGED")
        proof = staging / "coordinated-proof.json"
        url = f'https://github.com/{contract.REPOSITORY}/releases/download/frontend-{manifest["revision"]}/coordinated-proof.json'
        self.publisher.transport(url, proof, 4096)
        ensure(proof.is_file() and not proof.is_symlink() and proof.stat().st_size <= 4096 and
               json.loads(proof.read_text(encoding="utf-8")) == pair["proof"], "FRONTEND_PROOF_CHANGED")
        return manifest

    def prepare(self, previous, candidate):
        """Verify the active pair and stage the protected candidate without switching."""
        self._current(previous)
        ensure(not self.publisher.journal.exists() and not self.publisher.journal.is_symlink(), "FRONTEND_RECOVERY_REQUIRED")
        state = self.publisher.load()
        ensure(state["phase"] == "healthy" and state["error"] is None, "FRONTEND_NOT_HEALTHY")
        ensure(self.publisher.verify(previous["frontend"]["revision"], previous["backend"]["revision"]) == previous["frontend"],
               "FRONTEND_MANIFEST_CHANGED")
        with tempfile.TemporaryDirectory(prefix="pair-stage-", dir=self.publisher.root) as temporary:
            staging = Path(temporary)
            manifest = self._approved(candidate, staging)
            ensure(shutil.disk_usage(self.publisher.root).free >= contract.MAX_COMPRESSED + contract.MAX_EXTRACTED + hosting.DISK_RESERVE,
                   "INSUFFICIENT_DISK")
            extracted = self.publisher.materialize(manifest, staging)
            revision = manifest["revision"]
            target = self.publisher.releases / revision
            if target.exists() or target.is_symlink():
                ensure(hosting.fingerprints(target) == hosting.fingerprints(extracted), "FRONTEND_FILES_CHANGED")
            else:
                extracted.replace(target)
                hosting.sync_directory(self.publisher.releases)
            manifests = self.publisher.root / "manifests"
            manifests.mkdir(mode=0o700, exist_ok=True)
            path = manifests / (revision + ".json")
            if path.exists() or path.is_symlink():
                ensure(self.publisher.manifest(revision, candidate["backend"]["revision"]) == manifest,
                       "FRONTEND_MANIFEST_CHANGED")
            else:
                hosting.atomic_json(path, manifest)

    def activate(self, previous, candidate):
        """Switch only after the exact candidate backend is installed and gate is closed."""
        self.gate.verify()
        ensure(hosting.backend_revision(self.publisher.backend_state) == candidate["backend"]["revision"], "BACKEND_CHANGED")
        ensure(hosting.current_revision(self.publisher.releases) == previous["frontend"]["revision"], "FRONTEND_CHANGED")
        self.verify_retained(previous)
        self.verify_retained(candidate)
        with tempfile.TemporaryDirectory(prefix="pair-approved-", dir=self.publisher.root) as temporary:
            self._approved(candidate, Path(temporary))
        state = self.publisher.load()
        ensure(state["phase"] == "healthy" and state["error"] is None, "FRONTEND_NOT_HEALTHY")
        self.publisher.save(state, phase="activating", candidateRevision=candidate["frontend"]["revision"], checks={})
        hosting.switch(self.publisher.releases, candidate["frontend"]["revision"])
        manifest = self.publisher.verify(candidate["frontend"]["revision"], candidate["backend"]["revision"])
        ensure(manifest == candidate["frontend"], "FRONTEND_MANIFEST_CHANGED")
        self.publisher.finish(state, manifest, candidate["backend"]["revision"], previous["frontend"]["revision"], paused=True)

    def restore(self, previous, candidate):
        """Restore the original frontend only after its matching backend is active."""
        self.gate.verify()
        ensure(hosting.backend_revision(self.publisher.backend_state) == previous["backend"]["revision"], "BACKEND_CHANGED")
        active = hosting.current_revision(self.publisher.releases)
        ensure(active in (previous["frontend"]["revision"], candidate["frontend"]["revision"]), "FRONTEND_CHANGED")
        manifest = self.verify_retained(previous)
        state = self.publisher.load()
        hosting.switch(self.publisher.releases, manifest["revision"])
        ensure(self.publisher.verify(manifest["revision"], previous["backend"]["revision"]) == manifest,
               "FRONTEND_MANIFEST_CHANGED")
        self.publisher.finish(state, manifest, previous["backend"]["revision"], candidate["frontend"]["revision"], paused=True)
        self.publisher.save(state, phase="rolledBack", candidateRevision=candidate["frontend"]["revision"],
                            error="PUBLICATION_FAILED", paused=True, checks={"files": True, "https": True})

    def verify(self, pair):
        """Check the live local HTTPS bytes against the exact paired manifest."""
        self.gate.verify()
        self._current(pair)
        manifest = self.publisher.verify(pair["frontend"]["revision"], pair["backend"]["revision"])
        ensure(manifest == pair["frontend"], "FRONTEND_MANIFEST_CHANGED")
