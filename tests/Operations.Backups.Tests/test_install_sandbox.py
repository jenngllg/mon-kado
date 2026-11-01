"""Run the native installer in a private mount namespace, never in host production paths."""
import fcntl
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


@unittest.skipUnless(os.environ.get("MONKADO_NATIVE_BACKUP_INSTALL_TESTS") == "1", "Explicit native mount namespace installer exercise only")
class InstallSandboxTests(unittest.TestCase):
    def setUp(self):
        self.assertEqual(0, os.geteuid())
        self.source = Path(__file__).resolve().parents[2]
        self.temporary = tempfile.TemporaryDirectory(prefix="monkado-native-installer-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for name in ("etc/systemd/system", "etc/monkado-backup", "varlib/monkado-backup",
                     "varlib/monkado-deployment", "opt/monkado/src/Operations.Native",
                     "opt/monkado/deployments/production", "sbin", "commands"):
            (self.root / name).mkdir(parents=True)
        for name in ("src/Operations.Native/native_services.py", "deployments/production/release_manifest.py"):
            (self.root / "opt/monkado" / name).write_text("reviewed-fixture")
        self.calls = self.root / "calls.jsonl"
        for name in ("systemctl", "restic", "rclone"):
            command = self.root / "commands" / name
            command.write_text("#!/usr/bin/python3\nimport json,pathlib,sys\nwith pathlib.Path(" + repr(str(self.calls)) + ").open('a') as output:\n    output.write(json.dumps(sys.argv[1:])+'\\n')\n")
            command.chmod(0o755)
        self.environment = os.environ | {"PATH": str(self.root / "commands") + ":" + os.environ["PATH"]}
        # The child gets private propagation before bind mounts. No host target is created or removed.
        self.wrapper = self.root / "run.sh"
        self.wrapper.write_text("""#!/usr/bin/env bash
set -Eeuo pipefail
mount --make-rprivate /
mount --bind "$1/etc" /etc
mount --bind "$1/varlib" /var/lib
mount --bind "$1/opt" /opt
mount --bind "$1/sbin" /usr/local/sbin
exec bash "$2/deployments/backup/install.sh"
""")

    def execute(self):
        return subprocess.run(["unshare", "--mount", "bash", str(self.wrapper), str(self.root), str(self.source)],
                              capture_output=True, env=self.environment, timeout=15)

    def test_install_preserves_credentials_status_and_timer_activation(self):
        # Arrange
        secret = self.root / "etc/monkado-backup/password"
        secret.write_text("secret-canary")
        secret.chmod(0o600)
        status = self.root / "varlib/monkado-backup/status.json"
        status.write_text('{"error":"PREVIOUS_FAILURE"}')
        # Act
        result = self.execute()
        # Assert
        self.assertEqual(0, result.returncode, result.stderr.decode())
        self.assertEqual("secret-canary", secret.read_text())
        self.assertEqual(0o600, secret.stat().st_mode & 0o777)
        self.assertEqual('{"error":"PREVIOUS_FAILURE"}', status.read_text())
        for module in (self.source / "src/Operations.Backups/monkado_backup").glob("*.py"):
            self.assertEqual(module.read_bytes(), (self.root / "opt/monkado-backup/monkado_backup" / module.name).read_bytes())
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()]
        self.assertIn(["is-active", "--quiet", "postgresql@18-main.service"], calls)
        self.assertIn(["enable", "monkado-backup-recovery.service"], calls)
        self.assertFalse(any("start" in call or "stop" in call or "--now" in call for call in calls))
        self.assertNotIn("secret-canary", result.stdout.decode() + result.stderr.decode())

    def test_active_backup_or_deployment_blocks_code_installation(self):
        # Arrange / Act / Assert
        for name in ("monkado-backup/backup.lock", "monkado-deployment/deploy.lock"):
            with self.subTest(lock=name), (self.root / "varlib" / name).open("a") as lock:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                self.assertNotEqual(0, self.execute().returncode)
                self.assertFalse((self.root / "opt/monkado-backup/monkado_backup/cli.py").exists())
