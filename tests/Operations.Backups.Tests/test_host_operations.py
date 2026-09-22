"""Real temporary files and strict fake process boundaries for host orchestration."""

import json
import os
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import Mock, patch

from fixtures import capture
from monkado_backup import operations as host
from monkado_backup.capture import verify_manifest
from monkado_backup.policy import BackupError


class HostOperationsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.state = self.root / "state"
        self.state.mkdir()
        self.source = self.root / "source"
        self.source.mkdir()
        self.manifest = capture(self.source)
        self.deployment = self.root / "deployment"
        self.deployment.mkdir()
        (self.deployment / "current.env").write_text((self.source / "configuration/current.env").read_text())
        self.settings = self.root / "settings"
        self.settings.mkdir()
        for name, value in (("password", "x" * 64), ("rclone.conf", "fixture"),
                            ("repository", "rclone:monkado:MonKado-backups/production-v1")):
            path = self.settings / name
            path.write_text(value)
            path.chmod(0o600)
        self.environment = self.source / "configuration/production.env"
        self.environment.chmod(0o600)
        for name, value in (("STATE", self.state), ("ROOT", self.source / "configuration"),
                            ("SETTINGS", self.settings), ("DEPLOYMENT", self.deployment),
                            ("PRODUCTION_ENV", self.environment)):
            patcher = patch.object(host, name, value)
            patcher.start()
            self.addCleanup(patcher.stop)
        self.now = datetime(2026, 9, 21, tzinfo=timezone.utc)
        self.commands = []
        self.stopped = False
        self.run = Mock(side_effect=self.process)
        self.wait = Mock()
        self.operations = host.Operations(self.run, lambda: self.now, self.wait)

    def process(self, args, **kwargs):
        """Explicitly enumerate permitted fake external operations."""
        self.commands.append(args)
        if args[:2] == ["docker", "compose"]:
            tail = args[args.index("-f") + 4:]
            if "stop" in tail:
                self.stopped = True
                return ""
            if "ps" in tail:
                return "" if self.stopped and "--status" in tail else tail[-1]
        if args[:2] == ["docker", "start"]:
            self.stopped = False
            return ""
        if args[:3] == ["docker", "volume", "inspect"]:
            return str(self.source / ("images" if args[-1].endswith("gift_images") else "keys"))
        if args[:2] == ["docker", "inspect"]:
            if args[-1] == "caddy":
                return "caddy-image-id"
            if args[-1] == "caddy-image-id":
                return "caddy@sha256:" + "e" * 64
            return {"{{.Image}}": "image-id", "{{index .RepoDigests 0}}": "postgres@sha256:" + "d" * 64,
                    "{{.State.Running}}": "true"}[args[3]]
        if args[:2] == ["docker", "exec"]:
            script = args[-1]
            if "pg_database_size" in script:
                return "100"
            if "SHOW server_version" in script:
                return "18.6"
            if "pg_dump" in script:
                kwargs["output"].write(b"PGDMP fixture")
                return ""
        if args[0] == "curl":
            return "Healthy"
        raise AssertionError("Unexpected command: " + args[0])

    def test_capture_publishes_only_after_services_recover(self):
        # Arrange
        (self.state / "capture.new").mkdir()
        (self.state / "pending").mkdir()
        (self.state / "pending/old").write_text("old")
        # Act
        self.operations.capture()
        # Assert
        self.assertFalse(self.stopped)
        self.assertFalse((self.state / "maintenance.json").exists())
        self.assertFalse((self.state / "pending/old").exists())
        self.assertEqual(self.now.isoformat(), verify_manifest(self.state / "pending")["createdAt"])
        self.assertNotIn("lastRemoteSuccess", self.operations.state())
        self.wait.assert_not_called()

    def test_first_capture_without_pending_directory(self):
        # Arrange / Act
        self.operations.capture()
        # Assert
        self.assertTrue((self.state / "pending/manifest.json").is_file())

    def test_capture_preserves_completed_candidate_before_another_maintenance(self):
        # Arrange
        candidate = self.state / "capture.new"
        candidate.mkdir()
        manifest = capture(candidate)
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "INTERRUPTED_CAPTURE_REQUIRES_TRANSFER"):
            self.operations.capture()
        self.assertEqual(manifest, verify_manifest(candidate))
        self.assertFalse((self.state / "maintenance.json").exists())
        self.run.assert_not_called()

    def test_resume_promotes_valid_candidate_without_maintenance(self):
        # Arrange
        candidate = self.state / "capture.new"
        candidate.mkdir()
        manifest = capture(candidate)
        self.operations.transfer = Mock()
        # Act
        self.assertTrue(self.operations.resume_capture())
        self.assertTrue(self.operations.resume_capture())
        # Assert
        self.assertEqual(2, self.operations.transfer.call_count)
        self.assertEqual(manifest, verify_manifest(self.state / "pending"))
        self.assertFalse(candidate.exists())
        self.run.assert_not_called()

    def test_resume_without_capture_preserves_status_and_does_not_call_providers(self):
        # Arrange
        self.operations.update(error="PREVIOUS_FAILURE", lastRemoteSuccess=self.now.isoformat())
        previous = self.operations.state()
        self.operations.transfer = Mock()
        # Act
        result = self.operations.resume_capture()
        # Assert
        self.assertFalse(result)
        self.assertEqual(previous, self.operations.state())
        self.operations.transfer.assert_not_called()
        self.run.assert_not_called()

    def test_resume_refuses_outstanding_maintenance_or_deployment(self):
        # Arrange
        self.operations.transfer = Mock()
        for marker in (self.state / "maintenance.json", self.deployment / "in-progress.env"):
            marker.touch()
            # Act / Assert
            with self.subTest(marker=marker.name), self.assertRaisesRegex(BackupError, "RECOVERY_REQUIRED"):
                self.operations.resume_capture()
            marker.unlink()
        self.operations.transfer.assert_not_called()
        self.run.assert_not_called()

    def test_recover_starts_only_existing_application_containers_without_migrations(self):
        # Arrange
        (self.state / "maintenance.json").write_text(json.dumps({"services": list(host.SERVICES)}))
        self.stopped = True
        # Act
        self.operations.recover()
        # Assert
        self.assertIn(["docker", "start", "api", "worker", "caddy"], self.commands)
        self.assertFalse(any("migrations" in arguments for arguments in self.commands))
        self.assertFalse(any(arguments[:2] == ["docker", "compose"] and "start" in arguments for arguments in self.commands))
        self.assertFalse((self.state / "maintenance.json").exists())

    def test_recover_refuses_missing_or_ambiguous_container_without_clearing_marker(self):
        # Arrange
        marker = self.state / "maintenance.json"
        marker.write_text(json.dumps({"services": list(host.SERVICES)}))
        for identifiers in ("", "one two"):
            self.run.side_effect = None
            self.run.return_value = identifiers
            # Act / Assert
            with self.subTest(identifiers=identifiers), self.assertRaisesRegex(BackupError, "RECOVERY_CONTAINER"):
                self.operations.recover()
            self.assertTrue(marker.exists())

    def test_incomplete_deployment_prevents_all_container_calls(self):
        # Arrange
        (self.deployment / "in-progress.env").touch()
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "DEPLOYMENT_INCOMPLETE"):
            self.operations.capture()
        self.run.assert_not_called()

    def test_insufficient_space_never_stops_services(self):
        # Arrange / Act / Assert
        with patch.object(host.shutil, "disk_usage", return_value=Mock(free=1)), self.assertRaisesRegex(BackupError, "INSUFFICIENT_LOCAL_SPACE"):
            self.operations.capture()
        self.assertFalse(self.stopped)
        self.assertFalse((self.state / "maintenance.json").exists())

    def test_failed_dump_recovers_services_and_preserves_previous_capture(self):
        # Arrange
        pending = self.state / "pending"
        pending.mkdir()
        (pending / "old").touch()
        def failing(args, **kwargs):
            if "pg_dump" in args[-1]:
                raise BackupError("DUMP_FAILED")
            return self.process(args, **kwargs)
        self.run.side_effect = failing
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "DUMP_FAILED"):
            self.operations.capture()
        self.assertFalse(self.stopped)
        self.assertTrue((pending / "old").exists())
        self.assertNotIn("lastCapture", self.operations.state())

    def test_live_writers_abort_capture_and_recover(self):
        # Arrange
        def running(args, **kwargs):
            if "--status" in args:
                return "still-running"
            return self.process(args, **kwargs)
        self.run.side_effect = running
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "WRITERS_STILL_RUNNING"):
            self.operations.capture()
        self.assertFalse(self.stopped)

    def test_container_and_volume_resolution_fail_closed(self):
        # Arrange / Act / Assert
        for value in ("", "one two"):
            self.run.side_effect = None
            self.run.return_value = value
            with self.assertRaisesRegex(BackupError, "SERVICE_NOT_RUNNING"):
                self.operations.container("api")
        for value in ("relative", str(self.root / "missing")):
            self.run.return_value = value
            with self.assertRaisesRegex(BackupError, "INVALID_VOLUME"):
                self.operations.volume("gift_images")

    def test_recovery_failure_keeps_marker_for_operator(self):
        # Arrange
        marker = self.state / "maintenance.json"
        marker.write_text(json.dumps({"services": list(host.SERVICES)}))
        def not_running(args, **kwargs):
            if args[:2] == ["docker", "inspect"]:
                return "false"
            return self.process(args, **kwargs)
        self.run.side_effect = not_running
        # Act / Assert
        with self.assertRaisesRegex(BackupError, "SERVICE_RECOVERY_FAILED"):
            self.operations.recover()
        self.assertTrue(marker.exists())
        self.assertEqual(30, self.wait.call_count)

    def test_restic_receives_secret_paths_not_secret_values(self):
        # Arrange
        self.run.side_effect = None
        self.run.return_value = "success"
        # Act
        self.assertEqual("success", self.operations.restic("check"))
        # Assert
        call = self.run.call_args
        self.assertNotIn("x" * 64, repr(call))
        self.assertIn("--password-file", call.args[0])
        (self.settings / "repository").write_text("rclone:foreign:")
        with self.assertRaisesRegex(BackupError, "UNEXPECTED_REPOSITORY"):
            self.operations.restic("check")

    def test_weekly_check_reads_data(self):
        # Arrange
        self.operations.restic = Mock(return_value="")
        # Act
        self.operations.check()
        # Assert
        self.operations.restic.assert_called_once_with("check", "--read-data")
        self.assertEqual(self.now.isoformat(), self.operations.state()["lastIntegrityCheck"])

    def test_private_credentials_and_lock_contention(self):
        # Arrange / Act / Assert
        path = self.settings / "password"
        host.private_file(path)
        path.chmod(0o644)
        with self.assertRaisesRegex(BackupError, "UNSAFE_CREDENTIAL"):
            host.private_file(path)
        with host.lock(self.state / "lock"):
            with self.assertRaisesRegex(BackupError, "ALREADY_RUNNING"), host.lock(self.state / "lock"):
                self.fail("Concurrent lock accepted")

    def test_command_adapter_sanitizes_failure_and_timeout(self):
        # Arrange / Act / Assert
        self.assertEqual("ok\n", host.command([sys.executable, "-c", "print('ok')"]))
        with tempfile.TemporaryFile() as stream:
            self.assertEqual("", host.command([sys.executable, "-c", "print('ok')"], output=stream))
            stream.seek(0)
            self.assertEqual(b"ok\n", stream.read())
        with self.assertRaisesRegex(BackupError, "COMMAND_FAILED"):
            host.command([sys.executable, "-c", "import sys; sys.exit(1)"])
        with patch.object(host.subprocess, "run", side_effect=subprocess.TimeoutExpired("private", 1)), self.assertRaisesRegex(BackupError, "COMMAND_TIMEOUT"):
            host.command(["fake"])
        with patch.object(host, "datetime") as clock:
            clock.now.return_value = self.now
            self.assertEqual(self.now, host.utcnow())
