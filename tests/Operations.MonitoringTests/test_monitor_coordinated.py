"""Observe real kernel locks in isolated fixtures, without acquiring the publisher's lock."""

from datetime import timedelta
import fcntl
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

import monitor_deployment as deployment
from monitor_storage import atomic_json
from test_monitor_policy import NOW


@unittest.skipUnless(os.geteuid() == 0, "requires root-owned Linux fixtures")
class CoordinatedMonitorTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for name in ("monkado-deployment", "monkado-frontend"):
            (self.root / name).mkdir()
        self.lock = self.root / "monkado-deployment/deploy.lock"
        self.lock.touch(mode=0o600)
        self.runner = Mock(return_value=(0, "ActiveState=inactive\nResult=success\n"))

    def write_states(self, at=NOW, backend="verifying", frontend="activating", error=None):
        atomic_json(self.root / "monkado-deployment/status.json", {
            "schemaVersion": 1, "phase": backend, "error": error, "startedAt": at.isoformat()})
        atomic_json(self.root / "monkado-frontend/status.json", {
            "schemaVersion": 2, "phase": frontend, "error": error, "updatedAt": at.isoformat(), "paused": False})

    def observe(self, reader):
        return reader(self.root, self.runner, NOW, lambda started: NOW)

    def test_collect_when_coordinator_holds_lock_keeps_both_publications_active(self):
        # Arrange
        self.write_states()
        with self.lock.open("rb") as owner:
            fcntl.flock(owner, fcntl.LOCK_EX | fcntl.LOCK_NB)
            # Act / Assert
            for reader in (deployment.collect, deployment.collect_frontend):
                self.assertEqual({"failed": False, "recoveryRequired": False, "rollbackFailed": False}, self.observe(reader))
            # The observer must not release, replace or acquire the live lock.
            with self.lock.open("rb") as contender, self.assertRaises(BlockingIOError):
                fcntl.flock(contender, fcntl.LOCK_EX | fcntl.LOCK_NB)
        for reader in (deployment.collect, deployment.collect_frontend):
            self.assertTrue(self.observe(reader)["recoveryRequired"])

    def test_collect_when_active_coordinator_exceeds_deadline_or_records_failure_still_alerts(self):
        # Arrange
        with self.lock.open("rb") as owner:
            fcntl.flock(owner, fcntl.LOCK_EX | fcntl.LOCK_NB)
            # Act / Assert
            self.write_states(NOW - timedelta(seconds=901))
            for reader in (deployment.collect, deployment.collect_frontend):
                self.assertTrue(self.observe(reader)["recoveryRequired"])
            self.write_states(backend="recoveryRequired", frontend="recoveryRequired", error="ROLLBACK_FAILED")
            for reader in (deployment.collect, deployment.collect_frontend):
                self.assertEqual({"failed": True, "recoveryRequired": True, "rollbackFailed": True}, self.observe(reader))

    def test_collect_when_publisher_finishes_during_observation_rereads_terminal_state(self):
        # Arrange
        self.write_states()
        for reader, folder, phase in ((deployment.collect, "monkado-deployment", "succeeded"),
                                      (deployment.collect_frontend, "monkado-frontend", "healthy")):
            state_path = self.root / folder / "status.json"
            initial = json.loads(state_path.read_text())
            finished = initial | {"phase": phase}
            # Act / Assert
            with self.subTest(component=folder), patch.object(deployment, "read_json", side_effect=[initial, finished]) as read:
                self.assertFalse(self.observe(reader)["recoveryRequired"])
                self.assertEqual(2, read.call_count)

    def test_collect_when_lock_is_missing_or_unsafe_does_not_assume_running(self):
        # Arrange
        self.write_states()
        self.lock.unlink()
        # Act / Assert
        self.assertTrue(self.observe(deployment.collect)["recoveryRequired"])
        self.lock.touch(mode=0o644)
        with self.assertRaises(ValueError):
            self.observe(deployment.collect)
        self.lock.unlink()
        target = self.root / "elsewhere"
        target.touch(mode=0o600)
        self.lock.symlink_to(target)
        with self.assertRaises(OSError):
            self.observe(deployment.collect)

    def test_active_lock_when_kernel_evidence_is_missing_or_oversized_rejects_unknown(self):
        # Arrange
        table = self.root / "kernel-locks"
        # Act / Assert
        with self.assertRaises(FileNotFoundError):
            deployment.active_lock(self.lock, table)
        table.write_text(" " * 131073)
        with self.assertRaises(ValueError):
            deployment.active_lock(self.lock, table)
        table.write_text("1: -> FLOCK ADVISORY WRITE 7 0:0:0 0 EOF\n")
        self.assertFalse(deployment.active_lock(self.lock, table))
