"""Interactive private credential installation, with no production credentials in tests."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from deploy_policy import DeploymentError
from deploy_provision import provision
from test_functional_smoke import ACCOUNT


class ProvisionTests(unittest.TestCase):
    def test_verified_member_is_installed_only_after_logout(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            client = Mock(token="synthetic")
            read = Mock(side_effect=[ACCOUNT["memberId"], ACCOUNT["email"], ACCOUNT["password"], ACCOUNT["password"]])
            with patch("deploy_provision.FunctionalSmoke") as smoke, patch("deploy_provision.sys.stdin.isatty", return_value=True):
                self.assertTrue(provision(root, root, lambda: client, read)["credentialsInstalled"])
                smoke.return_value.login.assert_called_once()
            target = root / "deployment-smoke.json"
            self.assertEqual(ACCOUNT, json.loads(target.read_text()))
            self.assertEqual(0o600, target.stat().st_mode & 0o777)
            client.json.assert_called_once_with("DELETE", "/api/v1/auth/sessions/current", expected=204)
            client.clear.assert_called_once()
            with self.assertRaisesRegex(DeploymentError, "ALREADY_INSTALLED"):
                provision(root, root, lambda: client, read, terminal=True)
            target.unlink()
            target.symlink_to(root / "missing")
            with self.assertRaisesRegex(DeploymentError, "ALREADY_INSTALLED"):
                provision(root, root, lambda: client, read, terminal=True)

    def test_invalid_or_unverified_credentials_are_not_installed(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            client = Mock(token=None)
            with self.assertRaisesRegex(DeploymentError, "TERMINAL_REQUIRED"):
                provision(root, root, lambda: client, terminal=False)
            read = Mock(side_effect=[ACCOUNT["memberId"], ACCOUNT["email"], ACCOUNT["password"], "different"])
            with self.assertRaisesRegex(DeploymentError, "CREDENTIALS_INVALID"):
                provision(root, root, lambda: client, read, terminal=True)
            read.side_effect = [ACCOUNT["memberId"], ACCOUNT["email"], ACCOUNT["password"], ACCOUNT["password"]]
            with patch("deploy_provision.FunctionalSmoke") as smoke:
                smoke.return_value.login.side_effect = DeploymentError("SMOKE_IDENTITY_REFUSED")
                with self.assertRaisesRegex(DeploymentError, "IDENTITY_REFUSED"):
                    provision(root, root, lambda: client, read, terminal=True)
            self.assertFalse((root / "deployment-smoke.json").exists())
            client.clear.assert_called_once()
