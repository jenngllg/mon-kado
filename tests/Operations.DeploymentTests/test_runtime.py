"""Docker command boundaries, image extraction, timeouts and fresh-process evidence."""

from datetime import datetime, timedelta, timezone
import io
import json
import os
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from deploy_policy import DeploymentError
from deploy_process import command
from deploy_runtime import Runtime, timestamp
from deploy_storage import atomic_write
from release_catalog import identifiers
from test_engine import release
from test_functional_smoke import ACCOUNT, Api


def archive_for(value, directory=False):
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w") as archive:
        entry = tarfile.TarInfo("migration-catalog.json")
        if directory:
            entry.type = tarfile.DIRTYPE
            archive.addfile(entry)
        else:
            raw = json.dumps(value).encode()
            entry.size = len(raw)
            archive.addfile(entry, io.BytesIO(raw))
    return output.getvalue()


class ProcessTests(unittest.TestCase):
    def test_docker_uses_explicit_local_socket_not_the_operators_saved_context(self):
        with patch("deploy_process.subprocess.run", return_value=Mock(returncode=0)) as process:
            self.assertEqual(b"", command(["docker", "version"]))
        self.assertEqual(["docker", "--host", "unix:///var/run/docker.sock", "version"], process.call_args.args[0])
        self.assertNotIn("DOCKER_CONTEXT", process.call_args.kwargs["env"])
        self.assertNotIn("DOCKER_HOST", process.call_args.kwargs["env"])

    def test_real_bounded_subprocess_and_sanitized_errors(self):
        self.assertEqual(b"ok\n", command(["/bin/echo", "ok"]))
        for arguments, maximum, timeout in ((["/bin/false"], 100, 1), (["/bin/echo", "sensitive"], 1, 1),
                                             (["/does-not-exist"], 100, 1)):
            with self.subTest(arguments=arguments), self.assertRaises(DeploymentError) as result:
                command(arguments, maximum=maximum, timeout=timeout)
            self.assertNotIn("sensitive", str(result.exception))
        with patch("deploy_process.subprocess.run", side_effect=subprocess.TimeoutExpired("private", 1)):
            with self.assertRaisesRegex(DeploymentError, "^COMMAND_FAILED$"):
                command(["/bin/false"])


class RuntimeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.runner = Mock(return_value=b"")
        self.clients = Mock()
        self.runtime = Runtime(runner=self.runner, client_factory=self.clients, root=self.root,
                               state=self.root, settings=self.root, observability=self.root)
        self.release = release()

    def test_compose_and_application_lifecycle_never_replace_infrastructure(self):
        self.runtime.migrate(self.release)
        self.runtime.stop()
        self.runtime.start(self.release)
        calls = [call.args[0] for call in self.runner.call_args_list]
        self.assertIn("migrations", calls[0])
        self.assertIn("--no-deps", calls[0])
        self.assertEqual(["docker", "stop", "--time", "30", "mon-kado-caddy-1", "mon-kado-api-1", "mon-kado-worker-1"], calls[1])
        self.assertEqual(["api", "worker"], calls[2][-2:])
        self.assertEqual(["docker", "start", "mon-kado-caddy-1"], calls[3])
        self.assertFalse(any("down" in call or "postgres" in call for call in calls))
        self.assertNotIn("POSTGRES_PASSWORD", (self.root / "attempt-images.env").read_text())

    def test_active_image_identity_and_private_inspection(self):
        metadata = {"image": "sha256:" + "a" * 64, "running": True}
        self.runner.side_effect = [json.dumps(metadata).encode(), metadata["image"].encode()] * 2
        self.runtime.verify_active(self.release)
        self.assertEqual(4, self.runner.call_count)
        for call in self.runner.call_args_list:
            self.assertNotIn(".Config.Env", str(call))
        self.runner.side_effect = [json.dumps(metadata).encode(), b"wrong-image"]
        with self.assertRaisesRegex(DeploymentError, "ACTIVE_RELEASE_MISMATCH"):
            self.runtime.verify_active(self.release)

    def test_extracts_only_catalog_from_unstarted_container_and_removes_it(self):
        container = "a" * 64
        self.runner.side_effect = [container.encode(), archive_for(self.release["migrationCatalog"]), b""]
        self.runtime.catalog(self.release)
        self.assertEqual(["docker", "rm", "-v", container], self.runner.call_args.args[0])
        for value in (archive_for(self.release["migrationCatalog"], True), archive_for(release("b", True)["migrationCatalog"]), b"invalid tar"):
            self.runner.side_effect = [container.encode(), value, b""]
            with self.subTest(value=len(value)), self.assertRaises(DeploymentError):
                self.runtime.catalog(self.release)
            self.assertEqual(["docker", "rm", "-v", container], self.runner.call_args.args[0])
        self.runner.side_effect = [b"not-a-container"]
        with self.assertRaisesRegex(DeploymentError, "INVALID_CONTAINER_ID"):
            self.runtime.catalog(self.release)

    def test_history_is_ordered_and_contains_no_credentials(self):
        self.runner.return_value = b"20260101000000_Initial\n"
        self.assertEqual(identifiers(self.release["migrationCatalog"]), self.runtime.history())
        arguments = self.runner.call_args.args[0]
        self.assertIn("-X", arguments[-3])
        self.assertNotIn("PASSWORD", str(arguments))
        self.runner.return_value = b"sensitive garbage\n"
        with self.assertRaisesRegex(DeploymentError, "DATABASE_HISTORY_INVALID"):
            self.runtime.history()

    def test_telemetry_must_belong_to_new_process_and_be_private(self):
        path = self.root / "api" / "snapshot.json"
        path.parent.mkdir()
        now = datetime.now(timezone.utc)
        value = {"schemaVersion": 1, "service": "api", "bootId": "a" * 32, "createdAt": now.isoformat()}
        atomic_write(path, json.dumps(value).encode())
        os.chown(path, 1654, 1654)
        self.assertEqual("a" * 32, self.runtime.telemetry("api", (now - timedelta(seconds=1)).isoformat()))
        with self.assertRaisesRegex(DeploymentError, "TELEMETRY_STALE"):
            self.runtime.telemetry("api", (now + timedelta(seconds=1)).isoformat())
        path.chmod(0o644)
        with self.assertRaisesRegex(DeploymentError, "TELEMETRY_INVALID"):
            self.runtime.telemetry("api", now.isoformat())
        with self.assertRaisesRegex(DeploymentError, "INVALID_TELEMETRY_TIME"):
            timestamp("2026-01-01T00:00:00")

    def test_preflight_checks_credentials_disk_images_and_account_before_any_stop(self):
        self.runner.return_value = b"True\n"
        atomic_write(self.root / "production.env", b"synthetic configuration")
        atomic_write(self.root / "deployment-smoke.json", json.dumps(ACCOUNT).encode())
        atomic_write(self.root / "monitoring.json", b'{"frontendEnabled":true}')
        client = Mock(token="token")
        self.clients.return_value = client
        with patch.object(self.runtime, "compose"), patch.object(self.runtime, "catalog") as catalog, \
             patch.object(self.runtime, "container", return_value={"running": True, "oom": False, "id": "a", "image": "b", "restarts": 2}), \
             patch.object(self.runtime, "inspect", return_value=self.release["revision"]), \
             patch("deploy_runtime.shutil.disk_usage", return_value=Mock(free=3 * 1024 ** 3)), \
             patch("deploy_runtime.FunctionalSmoke") as smoke, \
             patch("deploy_runtime.smoke_technical.frontend", return_value={"revision": "f" * 40}):
            smoke.return_value.journal.exists.return_value = True
            self.runtime.preflight(self.release, self.release)
            catalog.assert_called_once_with(self.release)
            smoke.return_value.login.assert_called_once()
            smoke.return_value.cleanup.assert_called_once()
            self.assertEqual({"revision": "f" * 40}, self.runtime.frontend_marker)
            self.assertFalse(any("stop" in call.args[0] for call in self.runner.call_args_list))
            atomic_write(self.root / "monitoring.json", b'{"frontendEnabled":false}')
            client.token = None
            smoke.return_value.journal.exists.return_value = False
            self.runtime.preflight(self.release, None)

    def test_technical_sample_validates_every_container_and_frontend(self):
        self.runtime.frontend_marker = {"revision": "f" * 40}
        container = {"running": True, "oom": False, "restarts": 0, "startedAt": "now", "id": "a", "image": "b"}
        baseline = {"infrastructure": {name: {"id": "a", "image": "b", "restarts": 0} for name in ("caddy", "postgres")},
                    "frontend": self.runtime.frontend_marker}
        self.runtime.restore_baseline(baseline)
        for value in (None, {}, {"infrastructure": {}, "frontend": None}):
            with self.subTest(value=value), self.assertRaisesRegex(DeploymentError, "BASELINE_REQUIRED"):
                self.runtime.restore_baseline(value)
        self.clients.return_value.deadline = 100
        with patch.object(self.runtime, "verify_active"), patch.object(self.runtime, "container", return_value=container), \
             patch.object(self.runtime, "telemetry") as telemetry, patch("deploy_runtime.smoke_technical.redirect"), \
             patch("deploy_runtime.smoke_technical.check") as check, \
             patch("deploy_runtime.smoke_technical.frontend", return_value=self.runtime.frontend_marker):
            self.runtime.technical_sample(self.release, deadline=50)
            self.assertEqual(50, self.clients.return_value.deadline)
            self.assertEqual(2, telemetry.call_count)
            check.assert_called_once()
            self.runtime.frontend_marker = None
            self.runtime.infrastructure = {}
            self.runtime.technical_sample(self.release)
            self.runtime.restore_baseline(baseline)
            container["oom"] = True
            with self.assertRaisesRegex(DeploymentError, "CONTAINER_UNSTABLE"):
                self.runtime.technical_sample(self.release)
            container.update(oom=False, id="changed")
            with self.assertRaisesRegex(DeploymentError, "INFRASTRUCTURE_CHANGED"):
                self.runtime.technical_sample(self.release)

    def test_readiness_resets_streak_and_runs_functional_only_once(self):
        elapsed = [0]
        self.runtime.clock = lambda: elapsed[0]
        self.runtime.pause = lambda seconds: elapsed.__setitem__(0, elapsed[0] + seconds)
        with patch.object(self.runtime, "technical_sample", side_effect=[DeploymentError("WARMING_UP"), AttributeError("private"), ValueError(), None, None, None, None]), \
             patch("deploy_runtime.credentials", return_value=ACCOUNT), patch("deploy_runtime.FunctionalSmoke") as smoke:
            self.assertTrue(self.runtime.smoke(self.release)["functional"])
            smoke.return_value.run.assert_called_once()
        with patch.object(self.runtime, "technical_sample", side_effect=DeploymentError("CONTAINER_UNSTABLE")):
            with self.assertRaisesRegex(DeploymentError, "CONTAINER_UNSTABLE"):
                self.runtime.smoke(self.release)
        with patch.object(self.runtime, "technical_sample", side_effect=OSError()):
            with self.assertRaisesRegex(DeploymentError, "TECHNICAL_SMOKE_TIMEOUT"):
                self.runtime.smoke(self.release)

    def test_malformed_functional_identity_becomes_sanitized_smoke_failure(self):
        client = Api()
        client.identity = []
        self.clients.return_value = client
        self.runtime.pause = lambda seconds: None
        with patch.object(self.runtime, "technical_sample"), patch("deploy_runtime.credentials", return_value=ACCOUNT):
            with self.assertRaisesRegex(DeploymentError, "^SMOKE_CONTRACT_FAILED$"):
                self.runtime.smoke(self.release)
        self.assertIsNone(client.token)
        self.assertFalse((self.root / "smoke-journal.json").exists())


if __name__ == "__main__":
    unittest.main()
