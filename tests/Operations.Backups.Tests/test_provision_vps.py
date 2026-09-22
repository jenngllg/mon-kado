"""Private provisioning uses only synthetic files and never contacts Google."""

import contextlib
import io
import os
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch

import provision_vps
from monkado_backup.policy import BackupError


class ProvisionVpsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "rclone-source.conf"
        self.source.write_text("[monkado]\ntype = drive\nscope = drive.file\n")
        self.settings = self.root / "settings"
        self.settings.mkdir()
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.object(provision_vps, "SETTINGS", self.settings))
        self.stack.enter_context(patch.object(provision_vps, "Path", return_value=self.source))
        self.output = io.StringIO()
        self.stack.enter_context(contextlib.redirect_stdout(self.output))
        self.stack.enter_context(contextlib.redirect_stderr(self.output))

    def password(self):
        path = self.settings / "password"
        path.write_text("synthetic-password-only")
        path.chmod(0o600)

    def test_install_and_existing_credentials_preserve_private_files(self):
        # Arrange
        with patch.object(provision_vps, "install_password", side_effect=self.password) as prompt:
            # Act
            self.assertEqual(0, provision_vps.main())
            self.source.write_text("[monkado]\ntype = drive\nscope = drive.file\n")
            self.assertEqual(0, provision_vps.main())
            # Assert
            prompt.assert_called_once_with()
        self.assertFalse(self.source.exists())
        self.assertEqual(0o600, (self.settings / "rclone.conf").stat().st_mode & 0o777)
        self.assertNotIn("synthetic-password-only", self.output.getvalue())

    def test_invalid_source_or_scope_does_not_install(self):
        # Arrange / Act / Assert
        for contents in ("[wrong]\ntype=drive\nscope=drive.file\n",
                         "[monkado]\ntype=other\nscope=drive.file\n",
                         "[monkado]\ntype=drive\nscope=drive\n"):
            self.source.write_text(contents)
            with self.subTest(contents=contents):
                self.assertEqual(1, provision_vps.main())
        self.source.unlink()
        self.assertEqual(1, provision_vps.main())
        self.source.symlink_to(self.root / "missing")
        self.assertEqual(1, provision_vps.main())
        self.assertEqual([], list(self.settings.iterdir()))

    def test_non_root_and_expected_errors_are_sanitized(self):
        # Arrange / Act / Assert
        with patch.object(provision_vps.os, "geteuid", return_value=1000):
            self.assertEqual(1, provision_vps.main())
        for error, expected in (
            (BackupError("UNSAFE_CREDENTIAL_FILE"), "UNSAFE_CREDENTIAL_FILE"),
            (PermissionError("secret-canary"), "CREDENTIAL_FILE_PERMISSION_DENIED"),
            (FileExistsError("secret-canary"), "CREDENTIAL_FILE_ALREADY_EXISTS"),
            (FileNotFoundError("secret-canary"), "CREDENTIAL_FILE_MISSING"),
            (EOFError("secret-canary"), "CREDENTIAL_INPUT_INTERRUPTED"),
        ):
            with self.subTest(error=type(error).__name__), patch.object(provision_vps, "private_file", side_effect=error):
                self.assertEqual(1, provision_vps.main())
                self.assertIn(expected, self.output.getvalue())
        self.assertNotIn("secret-canary", self.output.getvalue())

    def test_command_boundary_exits_without_disclosing_credentials(self):
        # Arrange / Act / Assert
        with patch.object(os, "geteuid", return_value=1000), self.assertRaises(SystemExit) as result:
            runpy.run_path(str(Path(provision_vps.__file__)), run_name="__main__")
        self.assertEqual(1, result.exception.code)
