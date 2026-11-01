"""Test the installed root entrypoint only in a private native mount namespace."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest
from native_fixture import MountSandbox


@unittest.skipUnless(os.environ.get("MONKADO_DEPLOYMENT_SANDBOX") == "1",
                     "Explicit isolated native installer opt-in required")
class EntryPointTests(unittest.TestCase):
    def setUp(self):
        self.source = Path(__file__).resolve().parents[2]
        self.sandbox = MountSandbox()
        self.addCleanup(self.sandbox.close)
        self.root = self.sandbox.root / "opt/monkado"
        self.root.mkdir(parents=True, exist_ok=True)
        shutil.copytree(self.source / "src/Operations.Deployment", self.root / "src/Operations.Deployment", dirs_exist_ok=True)
        shutil.copytree(self.source / "deployments/production", self.root / "deployments/production", dirs_exist_ok=True)
        shutil.copytree(self.source / "src/Operations.Native", self.root / "src/Operations.Native", dirs_exist_ok=True)
        self.state = self.sandbox.root / "varlib/monkado-deployment"
        self.state.mkdir(mode=0o700, parents=True, exist_ok=True)

    def test_status_is_bounded_and_has_no_external_effects(self):
        result = self.sandbox.execute(["bash", "/opt/monkado/deployments/production/monkado-deploy", "status"],
                                      os.environ | {"PYTHONPATH": "/untrusted"})
        self.assertEqual(0, result.returncode)
        value = json.loads(result.stdout)
        self.assertEqual("idle", value["phase"])
        self.assertIsNone(value["revision"])
        self.assertEqual([], list(self.state.iterdir()))

    def test_incomplete_installation_refuses_rollout_without_raw_errors(self):
        result = self.sandbox.execute(["bash", "/opt/monkado/deployments/production/deploy.sh"])
        self.assertEqual(1, result.returncode)
        self.assertEqual("DEPLOYMENT_OPERATION_FAILED", json.loads(result.stdout)["code"])
        self.assertEqual(b"", result.stderr)
        self.assertEqual([], list(self.state.iterdir()))


if __name__ == "__main__":
    unittest.main()
