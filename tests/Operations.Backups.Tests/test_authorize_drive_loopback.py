"""OAuth consent and refresh boundaries are simulated; no provider traffic."""

import contextlib
from datetime import datetime, timezone
import importlib
import io
import json
import logging
from pathlib import Path
import runpy
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch


class AuthorizeDriveLoopbackTests(unittest.TestCase):
    def setUp(self):
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.temporary = self.stack.enter_context(tempfile.TemporaryDirectory())
        self.root = Path(self.temporary)
        self.source = self.root / "client.json"
        self.destination = self.root / "rclone.conf"
        self.client = {"client_id": "synthetic-id", "client_secret": "secret-canary",
                       "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                       "token_uri": "https://oauth2.googleapis.com/token"}
        self.source.write_text(json.dumps({"installed": self.client}))
        self.credentials = SimpleNamespace(refresh_token="refresh-canary", token="access-canary",
                                           expiry=datetime(2026, 9, 22, tzinfo=timezone.utc), refresh=Mock())
        self.flow = Mock()
        self.flow.run_local_server.return_value = self.credentials
        self.factory = Mock()
        self.factory.from_client_config.return_value = self.flow
        self.response = Mock()
        self.response.json.return_value = {"user": {"emailAddress": "monkado.app@gmail.com"}}
        self.session = Mock()
        self.session.get.return_value = self.response
        self.session_context = Mock()
        self.session_context.__enter__ = Mock(return_value=self.session)
        self.session_context.__exit__ = Mock(return_value=False)
        self.authorized_session = Mock(return_value=self.session_context)
        self.stack.enter_context(patch.dict(sys.modules, {
            "google_auth_oauthlib.flow": SimpleNamespace(InstalledAppFlow=self.factory),
            "google.auth.transport.requests": SimpleNamespace(Request=Mock(), AuthorizedSession=self.authorized_session),
        }))
        self.module = importlib.import_module("authorize_drive_loopback")
        self.stack.enter_context(patch.object(self.module, "InstalledAppFlow", self.factory))
        self.stack.enter_context(patch.object(self.module, "AuthorizedSession", self.authorized_session))
        self.stack.enter_context(patch.object(sys, "argv", ["authorize", str(self.source), str(self.destination)]))
        self.stack.enter_context(patch.object(logging, "disable"))
        self.output = io.StringIO()
        self.stack.enter_context(contextlib.redirect_stdout(self.output))
        self.stack.enter_context(contextlib.redirect_stderr(self.output))

    def test_consent_refresh_account_and_private_file_are_verified(self):
        # Arrange / Act
        self.assertEqual(0, self.module.main())
        # Assert
        self.factory.from_client_config.assert_called_once_with(
            {"installed": self.client}, ["https://www.googleapis.com/auth/drive.file"], autogenerate_code_verifier=True)
        self.assertEqual("127.0.0.1", self.flow.run_local_server.call_args.kwargs["host"])
        self.assertEqual(0, self.flow.run_local_server.call_args.kwargs["port"])
        self.credentials.refresh.assert_called_once()
        self.assertEqual(0o600, self.destination.stat().st_mode & 0o777)
        for secret in ("secret-canary", "refresh-canary", "access-canary"):
            self.assertNotIn(secret, self.output.getvalue())

    def test_existing_destination_is_never_overwritten(self):
        # Arrange
        self.destination.write_text("existing")
        # Act / Assert
        self.assertEqual(1, self.module.main())
        self.assertEqual("existing", self.destination.read_text())
        self.factory.from_client_config.assert_not_called()

    def test_untrusted_oauth_urls_do_not_receive_credentials(self):
        # Arrange / Act / Assert
        for name in ("auth_uri", "token_uri"):
            client = self.client | {name: "https://untrusted.invalid/"}
            self.source.write_text(json.dumps({"installed": client}))
            self.assertEqual(1, self.module.main())
        self.factory.from_client_config.assert_not_called()

    def test_missing_refresh_token_and_wrong_account_do_not_write_file(self):
        # Arrange
        self.credentials.refresh_token = None
        # Act / Assert
        self.assertEqual(1, self.module.main())
        self.credentials.refresh.assert_not_called()
        self.credentials.refresh_token = "refresh-canary"
        self.response.json.return_value = {"user": {"emailAddress": "other@example.invalid"}}
        self.assertEqual(1, self.module.main())
        self.assertFalse(self.destination.exists())

    def test_command_boundary_does_not_print_provider_exception(self):
        # Arrange
        self.flow.run_local_server.side_effect = RuntimeError("secret-canary")
        # Act / Assert
        with self.assertRaises(SystemExit) as result:
            runpy.run_path(self.module.__file__, run_name="__main__")
        self.assertEqual(1, result.exception.code)
        self.assertNotIn("secret-canary", self.output.getvalue())
