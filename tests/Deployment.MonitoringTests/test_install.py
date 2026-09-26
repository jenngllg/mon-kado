"""Execute the real installer only in an explicitly disposable root container."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest


@unittest.skipUnless(Path("/.dockerenv").exists() and os.environ.get("MONKADO_MONITOR_INSTALL_TESTS") == "1",
                     "Disposable installer container required")
class InstallTests(unittest.TestCase):
    def setUp(self):
        self.source = Path(__file__).resolve().parents[2]
        self.fixture = Path("/tmp/monkado-monitor-install")
        # These exact sandbox paths are never mounted from the host.
        for path in (self.fixture, Path("/opt/monkado"), Path("/etc/monkado"), Path("/var/lib/monkado-monitoring")):
            if path.exists():
                shutil.rmtree(path)
            path.mkdir(parents=True)
        Path("/etc/os-release").write_text('ID=ubuntu\nVERSION_ID="24.04"\n')
        self.calls = self.fixture / "calls.jsonl"
        self.environment = os.environ | {"PATH": str(self.fixture) + ":" + os.environ["PATH"]}
        stub = '''#!/usr/local/bin/python3
import json, pathlib, sys
root = pathlib.Path('/tmp/monkado-monitor-install')
with (root / 'calls.jsonl').open('a') as output:
    output.write(json.dumps(sys.argv[1:]) + '\\n')
arguments = sys.argv[1:]
if arguments[:1] == ['is-active']:
    active = arguments[-1] == 'docker' or ((root / 'active').exists() and arguments[-1] == 'monkado-monitor.timer')
    sys.exit(0 if active else 3)
'''
        for name in ("systemctl", "curl"):
            path = self.fixture / name
            path.write_text(stub)
            path.chmod(0o755)

    def execute(self):
        return subprocess.run(["bash", str(self.source / "deployments/production/install.sh")],
                              capture_output=True, env=self.environment, timeout=15)

    def test_install_binds_reviewed_files_without_enabling_monitor_or_replacing_private_settings(self):
        # Arrange
        secret = Path("/etc/monkado/monitoring-gmail.json")
        secret.write_text("PRIVATE_FIXTURE")
        secret.chmod(0o600)
        backup = Path("/var/lib/monkado-backup")
        backup.mkdir(exist_ok=True)
        sentinel = backup / "preserved.fixture"
        sentinel.write_text("backup-state")
        # Act
        result = self.execute()
        # Assert
        self.assertEqual(0, result.returncode, result.stderr.decode())
        installed = Path("/opt/monkado")
        for source in (self.source / "src/Operations.Frontend").glob("frontend_*.py"):
            target = installed / "src/Operations.Frontend" / source.name
            self.assertEqual(source.read_bytes(), target.read_bytes())
            self.assertEqual(0o644, target.stat().st_mode & 0o777)
        for source in (self.source / "src/Operations.Monitoring").glob("monitor_*.py"):
            self.assertEqual(source.read_bytes(), (installed / "src/Operations.Monitoring" / source.name).read_bytes())
        for name in ("monkado-monitor.service", "monkado-monitor.timer"):
            expected = (self.source / "deployments/monitoring" / name).read_bytes()
            self.assertEqual(expected, (installed / "deployments/monitoring" / name).read_bytes())
            self.assertEqual(expected, (Path("/etc/systemd/system") / name).read_bytes())
        configuration = Path("/etc/monkado/monitoring.json")
        self.assertFalse(json.loads(configuration.read_text())["notificationsEnabled"])
        self.assertEqual(0o600, configuration.stat().st_mode & 0o777)
        for service in ("api", "worker"):
            metadata = (Path("/var/lib/monkado-observability") / service).stat()
            self.assertEqual((1654, 1654, 0o700), (metadata.st_uid, metadata.st_gid, metadata.st_mode & 0o777))
        configuration.write_text("preserve operator configuration")
        self.assertEqual(0, self.execute().returncode)
        self.assertEqual("preserve operator configuration", configuration.read_text())
        self.assertEqual("PRIVATE_FIXTURE", secret.read_text())
        self.assertEqual("backup-state", sentinel.read_text())
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()]
        self.assertFalse(any("start" in call for call in calls))
        self.assertFalse(any(any("backup" in argument for argument in call) for call in calls))
        self.assertFalse(any("enable" in call and any("monitor" in argument for argument in call) for call in calls))
        self.assertNotIn("PRIVATE", result.stdout.decode())

    def test_install_refuses_active_monitor_before_touching_other_timers(self):
        # Arrange
        (self.fixture / "active").touch()
        # Act
        result = self.execute()
        # Assert
        self.assertNotEqual(0, result.returncode)
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()]
        self.assertFalse(any("stop" in call for call in calls))
        self.assertFalse(Path("/opt/monkado/src/Operations.Monitoring").exists())


if __name__ == "__main__":
    unittest.main()
