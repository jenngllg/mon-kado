"""Exercise rollout/recovery on private disposable filesystems and simulated providers."""

import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Frontend"))
import frontend_contract as contract
import frontend_runtime as runtime


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.state = self.root / "state"
        self.state.mkdir()
        self.releases = self.state / "releases"
        self.releases.mkdir()
        self.backend = self.root / "backend"
        self.backend.mkdir()
        (self.backend / "current.env").write_text("RELEASE_REVISION=" + "b" * 40 + "\n")
        self.dist = self.root / "dist"
        self.dist.mkdir()
        (self.dist / "index.html").write_text("<h1>fixture</h1>")
        (self.dist / "assets").mkdir()
        (self.dist / "assets/main-abcdefgh.js").write_text("// fixture")
        self.packages = {}
        self.approved = self.build("a" * 40)
        self.health = Mock()
        self.operation = runtime.Deployment(self.state, self.backend, "c" * 64, self.transport, self.health)

    def build(self, revision):
        output = self.root / ("package-" + revision)
        manifest = contract.package_build(self.dist, output, revision, "b" * 40, "c" * 64)
        self.packages[contract.archive_url(manifest)] = output / "frontend.tar.gz"
        return manifest

    def transport(self, url, destination, limit):
        if url == runtime.POINTER:
            destination.write_text(json.dumps({"tag_name": "frontend-production", "draft": False, "prerelease": False,
                                              "body": json.dumps(self.approved)}))
        else:
            shutil.copyfile(self.packages[url], destination)
        self.assertLessEqual(destination.stat().st_size, limit)

    def test_first_publication_is_atomic_and_repeated_pointer_is_unchanged(self):
        # Arrange / Act
        result = self.operation.deploy()
        repeated = self.operation.deploy()
        # Assert
        self.assertEqual("installed", result)
        self.assertEqual("unchanged", repeated)
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.health.assert_called_once_with("a" * 40, ["assets/main-abcdefgh.js"], False)
        self.assertFalse(self.operation.journal.exists())
        self.assertEqual("healthy", json.loads(self.operation.status_file.read_text())["state"])
        self.assertEqual(0o600, self.operation.status_file.stat().st_mode & 0o777)
        self.assertEqual(0o644, (self.releases / "current/index.html").stat().st_mode & 0o777)
        self.assertEqual([], list(self.state.glob("attempt-*")))

    def test_failed_smoke_restores_previous_and_requires_explicit_retry(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        self.health.side_effect = RuntimeError("synthetic upstream failure")
        # Act / Assert
        with self.assertRaises(RuntimeError):
            self.operation.deploy()
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.health.reset_mock()
        with self.assertRaises(ValueError):
            self.operation.deploy()
        self.health.assert_not_called()
        self.health.side_effect = None
        self.assertEqual("installed", self.operation.deploy(retry=True))
        self.assertEqual("d" * 40, runtime.current_revision(self.releases))
        self.assertEqual("installed", self.operation.deploy(approved=self.build("e" * 40)))

    def test_version_two_smoke_checks_legal_documents_and_google_setting(self):
        # Arrange
        for name in contract.LEGAL_PAGES:
            (self.dist / name).write_text("<h1>Synthetic legal page</h1>")
        output = self.root / "google-package"
        self.approved = contract.package_build(self.dist, output, "d" * 40, "b" * 40, "c" * 64, True)
        self.packages[contract.archive_url(self.approved)] = output / "frontend.tar.gz"
        # Act
        result = self.operation.deploy()
        # Assert
        self.assertEqual("installed", result)
        self.health.assert_called_once_with("d" * 40, ["assets/main-abcdefgh.js", "legal-notice", "privacy-policy", "terms-of-use"], True)

    def test_failed_first_publication_removes_pointer(self):
        # Arrange
        self.health.side_effect = RuntimeError("unavailable")
        # Act / Assert
        with self.assertRaises(RuntimeError):
            self.operation.deploy()
        self.assertIsNone(runtime.current_revision(self.releases))
        self.assertFalse(self.operation.journal.exists())

    def test_recovery_of_interruption_rolls_back_before_another_attempt(self):
        # Arrange
        self.operation.deploy()
        candidate = self.releases / ("d" * 40)
        candidate.mkdir()
        runtime.atomic_json(self.operation.journal, {"previous": "a" * 40, "revision": "d" * 40})
        runtime.switch(self.releases, "d" * 40)
        # Act
        self.operation.recover()
        # Assert
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.assertEqual("ROLLOUT_INTERRUPTED", json.loads(self.operation.status_file.read_text())["error"])
        self.operation.recover()
        self.assertFalse(self.operation.journal.exists())

    def test_download_disk_space_and_interrupted_backend_do_not_change_active_site(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        # Act / Assert
        with patch.object(runtime.shutil, "disk_usage", return_value=Mock(free=0)), self.assertRaises(ValueError):
            self.operation.deploy()
        with patch.object(self.operation, "transport", side_effect=OSError("interrupted")), self.assertRaises(OSError):
            self.operation.deploy()
        (self.backend / "in-progress.env").write_text("interrupted")
        with self.assertRaises(ValueError):
            self.operation.deploy()
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))

    def test_backup_or_backend_lock_prevents_capture_and_publication(self):
        # Arrange / Act / Assert
        with runtime.locks(self.backend), self.assertRaises(BlockingIOError):
            self.operation.deploy()
        with (self.backend / "deploy.lock").open("a") as lock:
            runtime.fcntl.flock(lock, runtime.fcntl.LOCK_EX | runtime.fcntl.LOCK_NB)
            with self.assertRaises(BlockingIOError):
                self.operation.deploy()
        self.assertIsNone(runtime.current_revision(self.releases))

    def test_changed_existing_immutable_directory_is_not_trusted(self):
        # Arrange
        target = self.releases / ("a" * 40)
        target.mkdir()
        (target / "index.html").write_text("not the approved artifact")
        # Act / Assert
        with self.assertRaises(ValueError):
            self.operation.deploy()
        self.assertEqual("not the approved artifact", (target / "index.html").read_text())
        self.assertIsNone(runtime.current_revision(self.releases))

    def test_prune_keeps_current_and_two_previous_but_not_unrelated_paths(self):
        # Arrange
        for index in range(5):
            path = self.releases / (str(index) * 40)
            path.mkdir()
            os.utime(path, ns=(index, index))
        foreign = self.releases / "operator-notes"
        foreign.mkdir()
        link = self.releases / ("f" * 40)
        link.symlink_to(foreign, target_is_directory=True)
        # Act
        runtime.prune(self.releases, ["0" * 40, "3" * 40, "4" * 40])
        # Assert
        self.assertEqual({"0" * 40, "3" * 40, "4" * 40, "f" * 40, "operator-notes"},
                         {path.name for path in self.releases.iterdir()})

    def test_success_history_excludes_failed_staging_and_rejects_corruption(self):
        # Arrange / Act
        self.operation.deploy()
        for character in ("d", "e", "f"):
            self.approved = self.build(character * 40)
            self.operation.deploy()
        # Assert
        self.assertEqual(["f" * 40, "e" * 40, "d" * 40], json.loads((self.state / "history.json").read_text()))
        self.assertFalse((self.releases / ("a" * 40)).exists())
        for history in ({}, ["bad"], ["a" * 40] * 4):
            (self.state / "history.json").write_text(json.dumps(history))
            with self.assertRaises(ValueError):
                self.operation.complete(self.approved, "b" * 40, "e" * 40)

    def test_current_pointer_and_metadata_must_not_escape_trusted_roots(self):
        # Arrange / Act / Assert
        current = self.releases / "current"
        current.mkdir()
        with self.assertRaises(ValueError):
            runtime.current_revision(self.releases)
        current.rmdir()
        for target in ("../other", "a" * 40):
            current.symlink_to(target, target_is_directory=True)
            with self.assertRaises(ValueError):
                runtime.current_revision(self.releases)
            current.unlink()
        for revision in ("bad", "a" * 40):
            with self.assertRaises(ValueError):
                runtime.switch(self.releases, revision)
        (self.backend / "current.env").write_text("RELEASE_REVISION=bad")
        with self.assertRaises(ValueError):
            runtime.backend_revision(self.backend)
        for transition in ({}, {"previous": None, "revision": "bad"}):
            runtime.atomic_json(self.operation.journal, transition)
            with self.assertRaises(ValueError):
                self.operation.recover()
        linked = self.dist / "assets/linked.js"
        linked.symlink_to(self.dist / "index.html")
        with self.assertRaises(ValueError):
            runtime.fingerprints(self.dist)
        linked.unlink()
        (self.dist / ".env").write_text("private canary")
        with self.assertRaises(ValueError):
            runtime.fingerprints(self.dist)
