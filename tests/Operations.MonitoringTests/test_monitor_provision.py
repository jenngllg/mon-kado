"""Credential handoff exercises use only fictional credentials in temporary directories."""

import contextlib
import io
import json
import os
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch

import monitor_provision as provision

SOURCE = """# Private fixture, not a real credential

DATABASE_PASSWORD=must-not-copy
GMAIL_CLIENT_ID=fake-client
GMAIL_CLIENT_SECRET='fake-secret'
GMAIL_REFRESH_TOKEN="fake-refresh"
GMAIL_SENDER_ADDRESS=monkado.app@gmail.com
DRIVE_REFRESH_TOKEN=must-not-copy-either
"""


class ProvisionTests(unittest.TestCase):
    def test_extract_accepts_literal_values_and_excludes_unrelated_secrets(self):
        # Arrange / Act
        value = provision.extract(SOURCE, "operator@example.test")
        # Assert
        self.assertEqual({"clientId": "fake-client", "clientSecret": "fake-secret", "refreshToken": "fake-refresh",
                          "sender": "monkado.app@gmail.com", "recipient": "operator@example.test"}, value)

    def test_extract_rejects_missing_duplicate_interpolated_or_malformed_settings(self):
        # Arrange / Act / Assert
        invalid = [SOURCE.replace("GMAIL_CLIENT_SECRET='fake-secret'", "GMAIL_CLIENT_SECRET"),
                   SOURCE + "GMAIL_CLIENT_ID=duplicate\n", SOURCE.replace("fake-secret", "${SECRET}"),
                   SOURCE.replace("'fake-secret'", "'unclosed"), SOURCE.replace("'fake-secret'", "'"),
                   SOURCE.replace("fake-refresh", ""), SOURCE.replace("fake-refresh", "bad\\escape"),
                   SOURCE.replace("fake-refresh", "fake # ambiguous comment"), ""]
        for source in invalid:
            with self.subTest(source=source), self.assertRaisesRegex(ValueError, "^INVALID_MONITORING_DATA$"):
                provision.extract(source, "operator@example.test")

    def test_install_is_private_and_replacement_requires_explicit_command(self):
        # Arrange
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / "source.env"
            source.write_text(SOURCE)
            source.chmod(0o600)
            destination = root / "monitoring.json"
            prompt = lambda _: "operator@example.test"
            # Act
            result = provision.provision(["install"], source, destination, prompt)
            # Assert
            self.assertEqual({"credentialsInstalled": True, "mailSent": False, "servicesStarted": False}, result)
            self.assertEqual(0o600, destination.stat().st_mode & 0o777)
            self.assertEqual(0, destination.stat().st_uid)
            self.assertEqual("operator@example.test", json.loads(destination.read_text())["recipient"])
            with self.assertRaises(ValueError):
                provision.provision(["install"], source, destination, prompt)
            provision.provision(["replace"], source, destination, lambda _: "replacement@example.test")
            self.assertEqual("replacement@example.test", json.loads(destination.read_text())["recipient"])

    def test_invalid_permissions_symlinks_and_confirmation_never_replace_credentials(self):
        # Arrange
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / "source.env"
            source.write_text(SOURCE)
            source.chmod(0o600)
            destination = root / "monitoring.json"
            destination.write_text("original")
            prompt = lambda _: "operator@example.test"
            # Act / Assert
            for arguments in ([], ["install", "secret"]):
                with self.assertRaises(ValueError):
                    provision.provision(arguments, source, destination, prompt)
            with patch.object(provision.os, "geteuid", return_value=1000), self.assertRaises(ValueError):
                provision.provision(["replace"], source, destination, prompt)
            root.chmod(0o777)
            with self.assertRaises(ValueError):
                provision.provision(["replace"], source, destination, prompt)
            root.chmod(0o700)
            answers = iter(["one@example.test", "two@example.test"])
            with self.assertRaises(ValueError):
                provision.provision(["replace"], source, destination, lambda _: next(answers))
            source.chmod(0o644)
            with self.assertRaises(ValueError):
                provision.provision(["replace"], source, destination, prompt)
            self.assertEqual("original", destination.read_text())
            link = root / "symlink.json"
            link.symlink_to(destination)
            with self.assertRaises(ValueError):
                provision.provision(["replace"], source, link, prompt)
            with self.assertRaises(OSError):
                provision.read_source(link)

    def test_source_must_be_regular_root_owned_and_bounded(self):
        # Arrange
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / "source.env"
            source.write_text("x" * 65537)
            source.chmod(0o600)
            # Act / Assert
            with self.assertRaises(ValueError):
                provision.read_source(source)
            source.write_text(SOURCE)
            os.chown(source, 1000, 1000)
            with self.assertRaises(ValueError):
                provision.read_source(source)
            fifo = Path(folder) / "fifo"
            os.mkfifo(fifo, 0o600)
            with self.assertRaises(ValueError):
                provision.read_source(fifo)
            os.chown(source, 0, 0)
            metadata = source.stat()
            source.write_text("x" * 65537)
            with patch.object(provision.os, "fstat", return_value=metadata), self.assertRaises(ValueError):
                provision.read_source(source)

    def test_main_does_not_print_exception_details_and_entrypoint_is_guarded(self):
        # Arrange / Act
        output = io.StringIO()
        with patch.object(provision, "provision", return_value={"credentialsInstalled": True}), contextlib.redirect_stdout(output):
            result = provision.main(["install"])
        # Assert
        self.assertEqual(0, result)
        self.assertEqual({"credentialsInstalled": True}, json.loads(output.getvalue()))
        output = io.StringIO()
        with patch.object(provision, "provision", side_effect=ValueError("private@example.test")), contextlib.redirect_stdout(output):
            self.assertEqual(1, provision.main(["install"]))
        self.assertEqual({"error": "MONITOR_CREDENTIAL_SETUP_FAILED"}, json.loads(output.getvalue()))
        with patch("sys.argv", ["monitor_provision.py", "invalid"]), contextlib.redirect_stdout(io.StringIO()), self.assertRaises(SystemExit) as error:
            runpy.run_module("monitor_provision", run_name="__main__")
        self.assertEqual(1, error.exception.code)
