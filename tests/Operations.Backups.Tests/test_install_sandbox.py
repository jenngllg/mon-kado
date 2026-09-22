"""Exercise the real installer only inside a disposable, explicitly enabled container."""

import fcntl
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest


@unittest.skipUnless(Path("/.dockerenv").exists() and os.environ.get("MONKADO_BACKUP_INSTALL_TESTS") == "1",
                     "Disposable root installer container required")
class InstallSandboxTests(unittest.TestCase):
    def setUp(self):
        self.source = Path(__file__).resolve().parents[2]
        self.fixture = Path("/tmp/monkado-backup-install-test")
        for path in (self.fixture, Path("/etc/monkado-backup"), Path("/var/lib/monkado-backup"),
                     Path("/opt/monkado-backup"), Path("/var/lib/monkado-deployment")):
            if path.exists():
                shutil.rmtree(path)
            path.mkdir(parents=True)
        Path("/etc/systemd/system").mkdir(parents=True, exist_ok=True)
        self.calls = self.fixture / "calls.jsonl"
        self.environment = os.environ | {"PATH": str(self.fixture) + ":" + os.environ["PATH"]}
        stub = '''#!/usr/local/bin/python3
import json, pathlib, sys
root = pathlib.Path('/tmp/monkado-backup-install-test')
with (root / 'calls.jsonl').open('a') as output:
    output.write(json.dumps(sys.argv[1:]) + '\\n')
'''
        path = self.fixture / "systemctl"
        path.write_text(stub)
        path.chmod(0o755)

    def execute(self):
        return subprocess.run(["bash", str(self.source / "deployments/backup/install.sh")],
                              capture_output=True, env=self.environment, timeout=15)

    def test_install_preserves_credentials_status_and_timer_activation(self):
        # Arrange
        secret = Path("/etc/monkado-backup/password")
        secret.write_text("secret-canary")
        secret.chmod(0o600)
        status = Path("/var/lib/monkado-backup/status.json")
        status.write_text('{"error":"PREVIOUS_FAILURE"}')
        # Act
        result = self.execute()
        # Assert
        self.assertEqual(0, result.returncode, result.stderr.decode())
        self.assertEqual("secret-canary", secret.read_text())
        self.assertEqual(0o600, secret.stat().st_mode & 0o777)
        self.assertEqual('{"error":"PREVIOUS_FAILURE"}', status.read_text())
        for module in (self.source / "src/Operations.Backups/monkado_backup").glob("*.py"):
            self.assertEqual(module.read_bytes(), (Path("/opt/monkado-backup/monkado_backup") / module.name).read_bytes())
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()]
        self.assertIn(["enable", "monkado-backup-recovery.service"], calls)
        self.assertFalse(any("start" in call or "stop" in call or "--now" in call for call in calls))
        self.assertNotIn("secret-canary", result.stdout.decode() + result.stderr.decode())

    def test_active_backup_or_deployment_blocks_code_installation(self):
        # Arrange / Act / Assert
        for name in ("/var/lib/monkado-backup/backup.lock", "/var/lib/monkado-deployment/deploy.lock"):
            with self.subTest(lock=name), open(name, "a") as lock:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                self.assertNotEqual(0, self.execute().returncode)
                self.assertFalse(Path("/opt/monkado-backup/monkado_backup/cli.py").exists())
