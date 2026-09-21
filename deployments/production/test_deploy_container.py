"""Run only inside a disposable Docker container, never on a real host.

Exercise the unmodified root deployment script with fake external commands.
"""

import importlib.util
import json
import os
import shutil
import subprocess
import unittest
from pathlib import Path


@unittest.skipUnless(Path("/.dockerenv").exists() and os.environ.get("MONKADO_DEPLOYMENT_SANDBOX") == "1",
                     "Requires an explicitly disposable deployment-test container")
class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.root = Path("/opt/monkado")
        self.state = Path("/var/lib/monkado-deployment")
        self.fixture = Path("/tmp/monkado-deployment-test")
        # These fixed paths exist only in the disposable test container.
        for directory in (self.root, self.state, self.fixture, Path("/etc/monkado")):
            if directory.exists():
                shutil.rmtree(directory)
            directory.mkdir(parents=True)
        source = Path(__file__).resolve().parents[2]
        for name in ("compose.yaml", "deployments/caddy/Caddyfile"):
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source / name, target)
        shutil.copytree(source / "deployments/production", self.root / "deployments/production")
        secret = Path("/etc/monkado/production.env")
        secret.write_text("POSTGRES_PASSWORD=not-a-real-secret\n")
        secret.chmod(0o600)
        spec = importlib.util.spec_from_file_location("installed_manifest", self.root / "deployments/production/release_manifest.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.manifest = {
            "schemaVersion": 1, "revision": "a" * 40,
            "configurationHash": module.configuration_hash(self.root),
            "apiImage": "ghcr.io/jenngllg/mon-kado-api@sha256:" + "b" * 64,
            "workerImage": "ghcr.io/jenngllg/mon-kado-worker@sha256:" + "c" * 64,
        }
        self.release = {"tag_name": "backend-production", "draft": False, "prerelease": False,
                        "body": json.dumps(self.manifest)}
        self.write_release()
        (self.fixture / "mode").write_text("success")
        stub = '''#!/usr/local/bin/python3
import json, pathlib, sys
base = pathlib.Path('/tmp/monkado-deployment-test')
command = pathlib.Path(sys.argv[0]).name
arguments = sys.argv[1:]
mode = (base / 'mode').read_text()
with (base / 'calls').open('a') as output:
    output.write(json.dumps([command, *arguments]) + '\\n')
if command == 'curl':
    if '-o' in arguments:
        if mode == 'network-failure':
            sys.exit(22)
        pathlib.Path(arguments[arguments.index('-o') + 1]).write_bytes((base / 'release.json').read_bytes())
    elif mode == 'readiness-failure':
        sys.exit(22)
elif command == 'docker':
    if 'pull' in arguments and mode == 'pull-failure':
        sys.exit(1)
    if 'run' in arguments and mode == 'migration-failure':
        sys.exit(1)
    if 'ps' in arguments:
        print('worker-test')
    if 'inspect' in arguments:
        if '{{.RestartCount}}' in arguments:
            print('1' if mode == 'worker-restart' else '0')
        else:
            print('false' if mode == 'worker-failure' else 'true')
'''
        for command in ("docker", "systemctl", "curl", "sleep"):
            target = Path("/usr/bin") / command
            target.write_text(stub)
            target.chmod(0o755)
        # The script clears PATH; use the container's Python through a fixed link.
        if not Path("/usr/bin/python3").exists():
            Path("/usr/bin/python3").symlink_to("/usr/local/bin/python3")

    def write_release(self):
        (self.fixture / "release.json").write_text(json.dumps(self.release))

    def deploy(self, mode="success"):
        (self.fixture / "mode").write_text(mode)
        return subprocess.run(["bash", str(self.root / "deployments/production/deploy.sh")],
                              text=True, capture_output=True, timeout=30)

    def calls(self):
        path = self.fixture / "calls"
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def test_success_and_repeat_are_idempotent(self):
        result = self.deploy()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue((self.state / "current.env").exists())
        self.assertFalse((self.state / "in-progress.env").exists())
        docker_calls = [call for call in self.calls() if call[0] == "docker"]
        self.assertTrue(any("migrations" in call and "run" in call for call in docker_calls))
        self.assertEqual(0, self.deploy().returncode)
        self.assertEqual(docker_calls, [call for call in self.calls() if call[0] == "docker"])
        self.assertNotIn("not-a-real-secret", result.stdout + result.stderr)
        health_calls = [call for call in self.calls() if call[0] == "curl" and "-o" not in call]
        self.assertEqual(3, len(health_calls))
        for call in health_calls:
            self.assertIn("--resolve", call)
            self.assertIn("api.monkado.fr:443:127.0.0.1", call)
            self.assertNotIn("--insecure", call)

    def test_download_and_pull_fail_before_stopping_services(self):
        for mode in ("network-failure", "pull-failure"):
            with self.subTest(mode=mode):
                result = self.deploy(mode)
                self.assertNotEqual(0, result.returncode)
                self.assertFalse((self.state / "in-progress.env").exists())
                self.assertFalse(any("stop" in call for call in self.calls()))

    def test_invalid_manifest_cannot_execute_docker(self):
        self.release["body"] = json.dumps({**self.manifest, "apiImage": "$(touch /tmp/unsafe)"})
        self.write_release()
        self.assertNotEqual(0, self.deploy().returncode)
        self.assertFalse(any(call[0] == "docker" for call in self.calls()))

    def test_migration_failure_blocks_automatic_retry(self):
        self.assertNotEqual(0, self.deploy("migration-failure").returncode)
        self.assertTrue((self.state / "in-progress.env").exists())
        before = [call for call in self.calls() if call[0] == "docker"]
        self.assertNotEqual(0, self.deploy().returncode)
        self.assertEqual(before, [call for call in self.calls() if call[0] == "docker"])
        self.assertFalse((self.state / "current.env").exists())

    def test_readiness_failure_is_not_success(self):
        self.assertNotEqual(0, self.deploy("readiness-failure").returncode)
        self.assertTrue((self.state / "in-progress.env").exists())
        self.assertFalse((self.state / "current.env").exists())

    def test_returning_to_previous_release_does_not_hide_interrupted_rollout(self):
        self.assertEqual(0, self.deploy().returncode)
        previous_release = dict(self.release)
        self.release["body"] = json.dumps({**self.manifest, "revision": "d" * 40})
        self.write_release()
        self.assertNotEqual(0, self.deploy("migration-failure").returncode)
        self.release = previous_release
        self.write_release()
        result = self.deploy()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("earlier rollout did not finish", result.stderr)

    def test_worker_failure_is_not_success(self):
        self.assertNotEqual(0, self.deploy("worker-failure").returncode)
        self.assertFalse((self.state / "current.env").exists())

    def test_worker_restart_is_not_success(self):
        self.assertNotEqual(0, self.deploy("worker-restart").returncode)
        self.assertFalse((self.state / "current.env").exists())

    def test_insecure_secret_permissions_are_rejected(self):
        Path("/etc/monkado/production.env").chmod(0o644)
        self.assertNotEqual(0, self.deploy().returncode)
        self.assertEqual([], self.calls())

    def test_parallel_deployment_is_refused(self):
        import fcntl
        with (self.state / "deploy.lock").open("w") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertNotEqual(0, self.deploy().returncode)
        self.assertEqual([], self.calls())


if __name__ == "__main__":
    unittest.main()
