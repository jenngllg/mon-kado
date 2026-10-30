"""Check that uncertain state cannot silently reopen public traffic at boot."""

from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_boot import guard
from deploy_policy import DeploymentError


class CoordinatedBootTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.state = Path(self.temporary.name)
        self.gate = Mock()
        self.gate.exists.return_value = False

    def test_guard_when_no_intent_and_no_table_keeps_normal_traffic(self):
        self.assertEqual(guard(self.state, self.gate), "open")
        self.gate.close.assert_not_called()

    def test_guard_when_intent_exists_closes_even_if_intent_is_corrupt(self):
        (self.state / "coordinated-transition.json").write_text("damaged")
        self.assertEqual(guard(self.state, self.gate), "closed")
        self.gate.close.assert_called_once_with()
        self.gate.exists.assert_not_called()

    def test_guard_when_intent_is_a_dangling_link_still_closes(self):
        (self.state / "coordinated-transition.json").symlink_to(self.state / "missing")
        self.assertEqual(guard(self.state, self.gate), "closed")
        self.gate.close.assert_called_once_with()

    def test_guard_when_gate_is_orphaned_verifies_and_never_opens_it(self):
        self.gate.exists.return_value = True
        self.assertEqual(guard(self.state, self.gate), "closed")
        self.gate.verify.assert_called_once_with()
        self.gate.open.assert_not_called()

    def test_guard_when_state_directory_is_missing_fails_closed(self):
        with self.assertRaises(DeploymentError):
            guard(self.state / "missing", self.gate)
        self.gate.assert_not_called()


if __name__ == "__main__":
    unittest.main()
