"""The recovery password can only arrive through a masked local prompt."""

import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from monkado_backup.credentials import install_password
from monkado_backup.policy import BackupError


class CredentialsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.patcher = patch("monkado_backup.credentials.SETTINGS", self.root)
        self.patcher.start()
        self.addCleanup(self.patcher.stop)

    def test_private_file_is_created_once(self):
        # Arrange
        value = "aB7!" * 16
        prompt = Mock(side_effect=[value, value])
        # Act
        install_password(prompt)
        # Assert
        self.assertEqual(value, (self.root / "password").read_text())
        self.assertEqual(0o600, (self.root / "password").stat().st_mode & 0o777)
        with self.assertRaises(FileExistsError):
            install_password(Mock(side_effect=[value, value]))

    def test_invalid_input_never_creates_file(self):
        # Arrange / Act / Assert
        for first, second in (("x" * 32, "x" * 32), ("x" * 257, "x" * 257),
                              ("é" * 64, "é" * 64), ("x" * 63 + "\n", "x" * 63 + "\n"),
                              ("x" * 64, "y" * 64)):
            with self.subTest(length=len(first)), self.assertRaises(BackupError):
                install_password(Mock(side_effect=[first, second]))
        self.assertFalse((self.root / "password").exists())
