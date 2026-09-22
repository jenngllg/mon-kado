"""Workstation handoff tests never launch a browser or invoke rclone."""

import contextlib
import io
import json
from pathlib import Path
import runpy
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch

import authorize_drive


class AuthorizeDriveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "client.json"
        self.target = self.root / "rclone.conf"
        self.source.write_text(json.dumps({"installed": {"client_id": "test-client", "client_secret": "secret-canary"}}))
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.object(authorize_drive.sys, "argv", ["authorize", str(self.source), str(self.target), "/fake/rclone"]))
        self.output = io.StringIO()
        self.stack.enter_context(contextlib.redirect_stdout(self.output))
        self.stack.enter_context(contextlib.redirect_stderr(self.output))

    def test_authorization_uses_private_configuration_without_secret_arguments(self):
        # Arrange
        with patch.object(authorize_drive.subprocess, "run", return_value=SimpleNamespace(returncode=0)) as process:
            # Act
            self.assertEqual(0, authorize_drive.main())
            # Assert
            arguments = process.call_args.args[0]
            self.assertNotIn("secret-canary", " ".join(arguments))
            self.assertIn("reconnect", arguments)
            self.assertEqual(0o600, self.target.stat().st_mode & 0o777)
            self.assertIn("scope = drive.file", self.target.read_text())
            self.assertNotIn("secret-canary", self.output.getvalue())

    def test_failed_authorization_and_existing_destination_fail_closed(self):
        # Arrange
        with patch.object(authorize_drive.subprocess, "run", return_value=SimpleNamespace(returncode=1)) as process:
            # Act / Assert
            self.assertEqual(1, authorize_drive.main())
            self.assertEqual(1, authorize_drive.main())
            process.assert_called_once()

    def test_invalid_client_values_never_launch_external_process(self):
        # Arrange / Act / Assert
        for value in (None, "", "line\nbreak", "line\rbreak"):
            self.source.write_text(json.dumps({"installed": {"client_id": value, "client_secret": "secret-canary"}}))
            with self.subTest(value=value), patch.object(authorize_drive.subprocess, "run") as process:
                self.assertEqual(1, authorize_drive.main())
                process.assert_not_called()
        self.assertFalse(self.target.exists())

    def test_command_boundary_sanitizes_failure(self):
        # Arrange
        self.source.unlink()
        # Act / Assert
        with self.assertRaises(SystemExit) as result:
            runpy.run_path(authorize_drive.__file__, run_name="__main__")
        self.assertEqual(1, result.exception.code)
