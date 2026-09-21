"""Gmail transport tests never send mail or contact Google."""

import base64
from email import policy as email_policy
from email.parser import BytesParser
import json
import unittest
from unittest.mock import MagicMock, patch

import monitor_gmail as gmail
from test_monitor_policy import NOW


def credentials():
    return {"clientId": "fake-client", "clientSecret": "fake-secret", "refreshToken": "fake-refresh",
            "sender": "monkado.app@gmail.com", "recipient": "operator@example.test"}


class GmailTests(unittest.TestCase):
    def test_send_contains_only_technical_digest_and_uses_one_post_per_step(self):
        # Arrange
        calls = []
        def transport(url, payload, headers):
            calls.append((url, payload, headers))
            return {"access_token": "fake-access"} if len(calls) == 1 else {"id": "acknowledged"}
        incidents = {"api.unavailable": {"open": True, "changedAt": NOW.isoformat(), "ignored": "private"},
                     "host.oom": {"open": False, "changedAt": NOW.isoformat()}}
        # Act
        gmail.send(credentials(), incidents, list(incidents), NOW, transport)
        # Assert
        self.assertEqual(2, len(calls))
        self.assertEqual("Bearer fake-access", calls[1][2]["Authorization"])
        raw = json.loads(calls[1][1])["raw"]
        message = BytesParser(policy=email_policy.default).parsebytes(base64.urlsafe_b64decode(raw + "=" * (-len(raw) % 4)))
        self.assertEqual("operator@example.test", message["To"])
        body = message.get_content()
        self.assertIn("api.unavailable : incident ouvert", body)
        self.assertIn("host.oom : rétabli", body)
        for secret in ("private", "fake-secret", "fake-refresh", "fake-access"):
            self.assertNotIn(secret, body)

    def test_invalid_credentials_are_rejected_without_echoing_values(self):
        # Arrange / Act / Assert
        for invalid in ({}, credentials() | {"sender": "other@example.test"},
                        credentials() | {"recipient": "bad\naddress"}, credentials() | {"recipient": "no-address"},
                        credentials() | {"clientSecret": ""}):
            with self.subTest(invalid=invalid), self.assertRaisesRegex(ValueError, "^INVALID_MONITORING_DATA$"):
                gmail.credentials(invalid)

    def test_recipient_validation_handles_subdomains_and_rejects_ambiguous_labels(self):
        # Arrange / Act / Assert
        for recipient in ("operator+alerts@example.test", "operator@alerts.example.test"):
            with self.subTest(recipient=recipient):
                self.assertEqual(recipient, gmail.credentials(credentials() | {"recipient": recipient})["recipient"])
        for recipient in ("operator@example..test", "operator@.example.test", "operator@example.test.",
                          "operator@" + "a" * 4000, "operator@" + "a." * 1900):
            with self.subTest(recipient=recipient), self.assertRaisesRegex(ValueError, "^INVALID_MONITORING_DATA$"):
                gmail.credentials(credentials() | {"recipient": recipient})

    def test_ambiguous_send_is_not_retried_or_acknowledged(self):
        # Arrange
        calls = []
        def transport(url, payload, headers):
            calls.append(url)
            if len(calls) == 2:
                raise TimeoutError("private response")
            return {"access_token": "fake"}
        # Act / Assert
        with self.assertRaises(TimeoutError):
            gmail.send(credentials(), {"api.unavailable": {"open": True, "changedAt": NOW.isoformat()}},
                       ["api.unavailable"], NOW, transport)
        self.assertEqual(2, len(calls))

    def test_invalid_provider_ack_or_token_is_not_success(self):
        # Arrange / Act / Assert
        for token, ack in (({}, {}), ({"access_token": "bad\ntoken"}, {}), ({"access_token": "fake"}, {})):
            calls = []
            def transport(url, payload, headers):
                calls.append(url)
                return token if len(calls) == 1 else ack
            with self.subTest(token=token), self.assertRaises(ValueError):
                gmail.send(credentials(), {"host.oom": {"open": True, "changedAt": NOW.isoformat()}},
                           ["host.oom"], NOW, transport)

    def test_post_verifies_tls_disables_redirects_and_bounds_response(self):
        # Arrange
        opener = MagicMock()
        opener.open.return_value.__enter__.return_value.read.return_value = b'{"id":"ok"}'
        # Act
        with patch.object(gmail.urllib.request, "build_opener", return_value=opener) as build:
            result = gmail.post("https://oauth2.googleapis.com/token", b"fake", {})
        # Assert
        self.assertEqual({"id": "ok"}, result)
        self.assertIsInstance(build.call_args.args[0], gmail.NoRedirect)
        self.assertIsNone(build.call_args.args[0].redirect_request(None, None, 302, None, None, "https://other.test"))
        self.assertEqual(5, opener.open.call_args.kwargs["timeout"])
        self.assertEqual("POST", opener.open.call_args.args[0].method)
        opener.open.assert_called_once()
        opener.open.return_value.__enter__.return_value.read.return_value = b"x" * 65537
        with patch.object(gmail.urllib.request, "build_opener", return_value=opener), self.assertRaises(ValueError):
            gmail.post("https://oauth2.googleapis.com/token", b"fake", {})
        with self.assertRaises(ValueError):
            gmail.post("https://untrusted.test", b"fake", {})
