"""The opt-in channel test shares the alert quota and uses no real provider in tests."""

from contextlib import redirect_stdout
from datetime import timedelta
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

import monitor_cli as cli
import monitor_gmail as gmail
import monitor_runtime as runtime
from monitor_storage import atomic_json, read_json
from test_monitor_gmail import credentials
from test_monitor_policy import NOW


class EmailTestTests(unittest.TestCase):
    def test_channel_test_runs_without_observation_and_reserves_common_quota_before_send(self):
        # Arrange
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            collector = Mock()
            configuration = {"schemaVersion": 1, "frontendEnabled": False, "notificationsEnabled": False, "thresholds": {}}
            calls = []
            def notify(incidents, keys, now):
                self.assertEqual(len(calls) + 1, len(read_json(root / "state.json")["deliveries"]))
                self.assertFalse(read_json(root / "test-email.json")["emailAcknowledged"])
                calls.append(keys)
            observer = runtime.Monitor(root, collector, notify)
            # Act
            for _ in range(6):
                result = observer.run(configuration, NOW, test_email=True)
            # Assert
            self.assertTrue(result["emailAcknowledged"])
            self.assertEqual([["monitor.test"]] * 6, calls)
            self.assertIsNone(result["error"])
            collector.collect.assert_not_called()
            self.assertEqual({}, read_json(root / "state.json")["incidents"])
            with self.assertRaises(ValueError):
                observer.run(configuration, NOW, test_email=True)
            # A real incident shares this quota; the test cannot create a second allowance.
            due, recent = runtime.policy.notifications({"api.unavailable": {"pending": True, "attempts": 0,
                "nextAttemptAt": NOW.isoformat()}}, read_json(root / "state.json")["deliveries"], NOW,
                runtime.policy.configuration({}))
            self.assertEqual([], due)
            self.assertEqual(6, len(recent))
            calls.clear()
            observer.run(configuration, NOW + timedelta(hours=1), test_email=True)
            self.assertEqual(1, len(calls))

    def test_ambiguous_test_is_not_retried_or_marked_as_success(self):
        # Arrange
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            notifier = Mock(side_effect=TimeoutError("PRIVATE_PROVIDER_RESPONSE"))
            observer = runtime.Monitor(root, Mock(), notifier)
            configuration = {"schemaVersion": 1, "frontendEnabled": False, "notificationsEnabled": False, "thresholds": {}}
            # Act
            result = observer.run(configuration, NOW, test_email=True)
            # Assert
            notifier.assert_called_once()
            self.assertFalse(result["emailAcknowledged"])
            self.assertEqual("EMAIL_DELIVERY_UNCONFIRMED", result["error"])
            self.assertEqual(result, read_json(root / "test-email.json"))
            self.assertNotIn("PRIVATE", json.dumps(result))
            self.assertEqual([NOW.isoformat()], read_json(root / "state.json")["deliveries"])

    def test_cli_requires_explicit_command_and_returns_failure_for_unconfirmed_test(self):
        # Arrange
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            configuration_path = root / "settings.json"
            atomic_json(configuration_path, {"schemaVersion": 1, "frontendEnabled": False,
                                              "notificationsEnabled": False, "thresholds": {}})
            observer = Mock()
            observer.run.return_value = {"emailAcknowledged": False}
            # Act
            result = cli.execute(["test-email"], NOW, root, configuration_path, observer)
            # Assert
            self.assertEqual({"emailAcknowledged": False}, result)
            self.assertTrue(observer.run.call_args.kwargs["test_email"])
            with patch.object(cli, "execute", return_value=result), redirect_stdout(io.StringIO()):
                self.assertEqual(1, cli.main(["test-email"]))

    def test_gmail_test_message_is_not_presented_as_an_incident_recovery(self):
        # Arrange
        import base64
        from email import policy
        from email.parser import BytesParser
        transport = Mock(side_effect=[{"access_token": "fake"}, {"id": "ack"}])
        # Act
        gmail.send(credentials(), {"monitor.test": {"open": False, "changedAt": NOW.isoformat()}},
                   ["monitor.test"], NOW, transport)
        # Assert
        encoded = json.loads(transport.call_args.args[1])["raw"]
        message = BytesParser(policy=policy.default).parsebytes(base64.urlsafe_b64decode(encoded + "=" * (-len(encoded) % 4)))
        self.assertIn("Test volontaire du canal d’alerte", message.get_content())
        self.assertNotIn("rétabli", message.get_content())
        self.assertEqual(2, transport.call_count)
