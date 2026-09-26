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
        self.assertEqual(2, self.health.call_count)
        self.health.assert_called_with("a" * 40, self.releases / ("a" * 40), False)
        self.assertFalse(self.operation.journal.exists())
        self.assertEqual("healthy", self.operation.load()["phase"])
        self.assertEqual(0o600, self.operation.status_file.stat().st_mode & 0o777)
        self.assertEqual(0o644, (self.releases / "current/index.html").stat().st_mode & 0o777)
        self.assertEqual([], list(self.state.glob("attempt-*")))

    def test_failed_smoke_restores_previous_and_requires_explicit_retry(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        self.health.side_effect = [None, RuntimeError("synthetic upstream failure"), None]
        # Act / Assert
        with self.assertRaises(RuntimeError):
            self.operation.deploy()
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.health.reset_mock()
        self.assertEqual("rolledBack", self.operation.load()["phase"])
        self.assertTrue(self.operation.load()["paused"])
        self.assertEqual("paused", self.operation.deploy())
        self.assertEqual("paused", self.operation.deploy(retry=True))
        self.health.assert_not_called()
        self.health.side_effect = None
        self.operation.resume("d" * 40)
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
        self.health.assert_called_once_with("d" * 40, self.releases / ("d" * 40), True)

    def test_failed_first_publication_removes_pointer(self):
        # Arrange
        self.health.side_effect = RuntimeError("unavailable")
        # Act / Assert
        with self.assertRaises(RuntimeError):
            self.operation.deploy()
        self.assertIsNone(runtime.current_revision(self.releases))
        self.assertFalse(self.operation.journal.exists())

    def test_manual_rollback_can_replace_an_unhealthy_active_site(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        self.operation.deploy()
        self.health.reset_mock()
        def healthy_fallback_only(revision, directory, google_enabled):
            if revision != "a" * 40:
                raise ValueError("Current site unavailable")
        self.health.side_effect = healthy_fallback_only
        # Act
        result = self.operation.rollback("a" * 40)
        # Assert
        self.assertEqual("installed", result)
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.assertTrue(self.operation.load()["paused"])
        self.health.assert_called_once_with("a" * 40, self.releases / ("a" * 40), False)

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
        self.assertEqual("PUBLICATION_FAILED", self.operation.load()["error"])
        self.assertTrue(self.operation.load()["paused"])
        self.operation.recover()
        self.assertFalse(self.operation.journal.exists())

    def test_download_disk_space_and_interrupted_backend_do_not_change_active_site(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        # Act / Assert
        with patch.object(runtime.shutil, "disk_usage", return_value=Mock(free=0)), self.assertRaises(ValueError):
            self.operation.deploy()
        self.operation.resume("d" * 40)
        with patch.object(self.operation, "transport", side_effect=OSError("interrupted")), self.assertRaises(OSError):
            self.operation.deploy()
        self.operation.resume("d" * 40)
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
        (self.state / "history.json").write_text(" " * 4097)
        with self.assertRaises(ValueError):
            self.operation.history()
        (self.state / "history.json").unlink()
        (self.state / "history.json").symlink_to(self.root / "missing")
        with self.assertRaises(ValueError):
            self.operation.history()

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

    def test_manual_pause_survives_a_new_process_and_retry_cannot_bypass_it(self):
        # Arrange
        self.operation.deploy()
        # Act
        self.assertEqual("paused", self.operation.pause())
        restarted = runtime.Deployment(self.state, self.backend, "c" * 64, self.transport, self.health)
        # Assert
        self.assertEqual("paused", restarted.deploy(retry=True))
        self.assertIsNone(restarted.load()["error"])
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))

    def test_manual_rollback_remains_paused_until_exact_approved_manifest_is_resumed(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        self.operation.deploy()
        # Act
        self.assertEqual("installed", self.operation.rollback("a" * 40))
        # Assert
        self.assertTrue(self.operation.load()["paused"])
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.assertEqual("paused", self.operation.deploy(retry=True))
        with self.assertRaises(ValueError):
            self.operation.resume("a" * 40)
        self.operation.resume("d" * 40)
        self.approved = self.build("e" * 40)
        with self.assertRaises(ValueError):
            self.operation.deploy()
        self.assertTrue(self.operation.load()["paused"])
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))

    def test_rollback_incompatible_backend_and_unknown_history_never_switch(self):
        # Arrange
        self.operation.deploy()
        # Act / Assert
        (self.backend / "current.env").write_text("RELEASE_REVISION=" + "f" * 40 + "\n")
        with self.assertRaises(ValueError):
            self.operation.rollback("a" * 40)
        self.assertTrue(self.operation.load()["paused"])
        (self.backend / "current.env").write_text("RELEASE_REVISION=" + "b" * 40 + "\n")
        (self.state / "history.json").write_text("[]")
        with self.assertRaises(ValueError):
            self.operation.rollback("a" * 40)
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))

    def test_failed_restoration_keeps_journal_and_requires_explicit_recovery(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        self.health.side_effect = [None, RuntimeError("candidate"), RuntimeError("fallback")]
        # Act / Assert
        with self.assertRaises(RuntimeError):
            self.operation.deploy()
        self.assertEqual("recoveryRequired", self.operation.load()["phase"])
        self.assertEqual("ROLLBACK_FAILED", self.operation.load()["error"])
        self.assertTrue(self.operation.journal.exists())
        self.assertEqual("paused", self.operation.deploy(retry=True))
        with self.assertRaises(ValueError):
            self.operation.resume("d" * 40)
        self.health.side_effect = None
        self.operation.recover()
        self.assertEqual("rolledBack", self.operation.load()["phase"])
        self.assertFalse(self.operation.journal.exists())
        self.assertTrue(self.operation.load()["paused"])

    def test_interrupted_transition_recovery_stops_before_another_publication(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        with patch.object(runtime, "switch", side_effect=KeyboardInterrupt), self.assertRaises(KeyboardInterrupt):
            self.operation.deploy()
        # Act
        self.assertEqual("recovered", self.operation.deploy())
        # Assert
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.assertTrue(self.operation.load()["paused"])
        self.assertFalse(self.operation.journal.exists())

    def test_success_committed_before_journal_removal_is_reverified_not_rolled_back(self):
        # Arrange
        self.operation.deploy()
        runtime.atomic_json(self.operation.journal, {"previous": None, "revision": "a" * 40})
        self.health.reset_mock()
        # Act
        self.operation.recover()
        # Assert
        self.assertEqual("healthy", self.operation.load()["phase"])
        self.assertTrue(self.operation.load()["paused"])
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.health.assert_called_once()
        self.assertFalse(self.operation.journal.exists())

    def test_preparation_power_loss_latches_failure_without_a_switch(self):
        # Arrange
        self.operation.deploy()
        self.approved = self.build("d" * 40)
        self.health.side_effect = KeyboardInterrupt()
        with self.assertRaises(KeyboardInterrupt):
            self.operation.deploy()
        self.health.side_effect = None
        # Act / Assert
        self.assertEqual("paused", self.operation.deploy())
        self.assertEqual("ROLLOUT_INTERRUPTED", self.operation.load()["error"])
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))

    def test_first_publication_can_resume_without_any_existing_site(self):
        # Arrange
        self.operation.pause()
        # Act
        self.operation.resume("a" * 40)
        self.operation.deploy()
        # Assert
        self.assertEqual("healthy", self.operation.load()["phase"])
        self.assertFalse(self.operation.load()["paused"])

    def test_unchanged_site_cannot_hide_modified_bytes(self):
        # Arrange
        self.operation.deploy()
        (self.releases / "current/index.html").write_text("unexpected bytes")
        # Act / Assert
        with self.assertRaises(ValueError):
            self.operation.deploy()
        self.assertTrue(self.operation.load()["paused"])
        self.assertEqual("failed", self.operation.load()["phase"])

    def test_corrupt_journal_latches_recovery_without_switch_or_automatic_retry(self):
        # Arrange
        self.operation.deploy()
        self.operation.journal.write_text("{}")
        # Act / Assert
        with self.assertRaises(ValueError):
            self.operation.deploy()
        self.assertEqual("recoveryRequired", self.operation.load()["phase"])
        self.assertTrue(self.operation.load()["paused"])
        self.assertEqual("paused", self.operation.deploy(retry=True))
        self.assertEqual("a" * 40, runtime.current_revision(self.releases))
        self.assertEqual("{}", self.operation.journal.read_text())
