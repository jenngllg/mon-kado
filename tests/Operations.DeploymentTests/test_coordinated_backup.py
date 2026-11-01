"""Exercise fresh backup proof sequencing without a real provider."""

from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Deployment", "src/Operations.Frontend", "deployments/production"):
    sys.path.insert(0, str(ROOT / path))
from coordinated_backup import capture
from deploy_policy import DeploymentError


class CoordinatedBackupTests(unittest.TestCase):
    def setUp(self):
        self.started = datetime(2026, 10, 30, 12, tzinfo=timezone.utc)
        self.clock = Mock(side_effect=[self.started, self.started + timedelta(minutes=3)])
        self.gate = Mock()
        stamp = (self.started + timedelta(minutes=1)).isoformat()
        self.state = {"snapshotId": "a" * 64, "error": None, "lastCapture": stamp, "lastRemoteCapture": stamp,
                      "lastRemoteSuccess": (self.started + timedelta(minutes=2)).isoformat(),
                      "lastIntegrityCheck": (self.started + timedelta(minutes=2)).isoformat()}
        self.operations = Mock(side_effect=lambda action, inherited: json.dumps(self.state).encode() if action == "status" else b"")

    def test_capture_when_valid_checks_gate_backup_remote_integrity_and_freshness(self):
        snapshot = capture(self.operations, self.clock, self.gate, inherited=True)
        self.assertEqual(snapshot, "a" * 64)
        self.assertEqual(self.gate.verify.call_count, 2)
        self.assertEqual([call.args for call in self.operations.call_args_list], [("backup",), ("check",), ("status",)])
        self.assertEqual([call.kwargs for call in self.operations.call_args_list],
                         [{"inherited": True}, {"inherited": False}, {"inherited": False}])

    def test_capture_when_remote_evidence_is_stale_refuses_snapshot(self):
        self.state["lastRemoteCapture"] = "wrong"
        with self.assertRaises(DeploymentError):
            capture(self.operations, self.clock, self.gate, inherited=True)
        self.assertEqual(self.gate.verify.call_count, 1)

    def test_capture_when_gate_verification_fails_never_starts_backup(self):
        self.gate.verify.side_effect = DeploymentError("TRAFFIC_GATE_INVALID")
        with self.assertRaises(DeploymentError):
            capture(self.operations, self.clock, self.gate, inherited=True)
        self.operations.assert_not_called()

    def test_capture_when_integrity_check_fails_never_accepts_status(self):
        self.operations.side_effect = [b"", DeploymentError("COORDINATED_BACKUP_FAILED")]
        with self.assertRaises(DeploymentError):
            capture(self.operations, self.clock, self.gate, inherited=True)
        self.assertEqual(self.operations.call_count, 2)


if __name__ == "__main__":
    unittest.main()
