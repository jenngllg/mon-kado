"""Test the installed root entrypoint only in an explicitly disposable container."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest


@unittest.skipUnless(Path("/.dockerenv").exists() and os.environ.get("MONKADO_DEPLOYMENT_SANDBOX") == "1",
                     "Requires an explicitly disposable deployment-test container")
class EntryPointTests(unittest.TestCase):
    def setUp(self):
        self.source = Path(__file__).resolve().parents[2]
        self.root = Path("/opt/monkado")
        self.root.mkdir(parents=True, exist_ok=True)
        shutil.copytree(self.source / "src/Operations.Deployment", self.root / "src/Operations.Deployment", dirs_exist_ok=True)
        shutil.copytree(self.source / "deployments/production", self.root / "deployments/production", dirs_exist_ok=True)
        self.state = Path("/var/lib/monkado-deployment")
        self.state.mkdir(mode=0o700, parents=True, exist_ok=True)
        if not Path("/usr/bin/python3").exists():
            Path("/usr/bin/python3").symlink_to("/usr/local/bin/python3")

    def test_status_is_bounded_and_has_no_external_effects(self):
        result = subprocess.run(["bash", str(self.root / "deployments/production/monkado-deploy"), "status"],
                                capture_output=True, timeout=10, env=os.environ | {"PYTHONPATH": "/untrusted", "DOCKER_HOST": "tcp://untrusted"})
        self.assertEqual(0, result.returncode)
        value = json.loads(result.stdout)
        self.assertEqual("idle", value["phase"])
        self.assertIsNone(value["revision"])
        self.assertEqual([], list(self.state.iterdir()))

    def test_incomplete_installation_refuses_rollout_without_raw_errors(self):
        result = subprocess.run(["bash", str(self.root / "deployments/production/deploy.sh")], capture_output=True, timeout=10)
        self.assertEqual(1, result.returncode)
        self.assertEqual("DEPLOYMENT_OPERATION_FAILED", json.loads(result.stdout)["code"])
        self.assertEqual(b"", result.stderr)
        self.assertEqual([], list(self.state.iterdir()))


if __name__ == "__main__":
    unittest.main()
