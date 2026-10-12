"""Native command boundaries, approved executables, timeouts and fresh-process evidence."""

from datetime import datetime, timedelta, timezone
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from deploy_policy import DeploymentError, dotenv
from deploy_process import command
from deploy_runtime import Runtime, timestamp
from deploy_storage import atomic_write
from release_catalog import identifiers
from test_engine import release
from test_functional_smoke import ACCOUNT, Api


class ProcessTests(unittest.TestCase):
    def test_native_commands_use_a_fixed_environment_without_operator_proxies_or_credentials(self):
        with patch("deploy_process.subprocess.run", return_value=Mock(returncode=0)) as process:
            self.assertEqual(b"", command(["systemctl", "is-active", "caddy.service"]))
        self.assertEqual(["systemctl", "is-active", "caddy.service"], process.call_args.args[0])
        self.assertEqual({"PATH", "LANG", "HOME", "TZ"}, set(process.call_args.kwargs["env"]))
        self.assertEqual("UTC", process.call_args.kwargs["env"]["TZ"])

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
        self.observer = Mock()
        self.runtime = Runtime(runner=self.runner, client_factory=self.clients, root=self.root,
                               state=self.root, settings=self.root, observability=self.root,
                               observer=self.observer, service_uid=os.getuid())
        self.release = release()

    def test_native_application_lifecycle_preserves_postgresql_and_verified_previous_releases(self):
        with patch("deploy_runtime.native_artifacts.verify_directory") as verify:
            self.runtime.migrate(self.release)
            self.runtime.stop()
            self.runtime.start(self.release)
            self.assertEqual(2, verify.call_count)
        calls = [call.args[0] for call in self.runner.call_args_list]
        self.assertEqual("systemd-run", calls[0][0])
        self.assertIn("--property=User=monkado", calls[0])
        self.assertIn("--property=EnvironmentFile=" + str(self.root / "production.env"), calls[0])
        self.assertEqual(str(self.runtime.directory(self.release) / "migrate"), calls[0][-1])
        self.assertEqual(["systemctl", "stop", "caddy.service", "mon-kado-api.service", "mon-kado-worker.service"], calls[1])
        self.assertEqual(["systemctl", "reset-failed", "mon-kado-api.service", "mon-kado-worker.service"], calls[2])
        self.assertEqual(["systemctl", "start", "mon-kado-api.service", "mon-kado-worker.service", "caddy.service"], calls[3])
        self.assertFalse(any("postgres" in argument for call in calls for argument in call))
        self.assertEqual(dotenv(self.release).encode(), (self.root / "active.env").read_bytes())
        self.assertEqual(self.runtime.directory(self.release), (self.root / "current").resolve())
        self.assertNotIn("DATABASE_URL", str(calls))

    def test_active_executable_identity_uses_only_the_native_observation_contract(self):
        metadata = {"image": "a" * 64, "running": True}
        self.observer.return_value = metadata
        self.runtime.verify_active(self.release)
        self.assertEqual(2, self.observer.call_count)
        self.runner.assert_not_called()
        metadata["image"] = "b" * 64
        with self.assertRaisesRegex(DeploymentError, "ACTIVE_RELEASE_MISMATCH"):
            self.runtime.verify_active(self.release)

    def test_catalog_is_read_only_through_verified_native_artifact_metadata(self):
        with patch("deploy_runtime.native_artifacts.inspect") as inspect:
            self.runtime.catalog(self.release)
        inspect.assert_called_once_with(self.runtime.directory(self.release), self.release, self.runner)

    def test_history_is_ordered_and_contains_no_credentials(self):
        self.runner.side_effect = [b"mon_kado_migrations\n", b"001_initial.sql\n"]
        self.assertEqual(identifiers(self.release["migrationCatalog"]), self.runtime.history())
        arguments = self.runner.call_args.args[0]
        self.assertEqual(["runuser", "-u", "postgres", "--", "psql", "-X", "--no-password", "-d", "mon_kado"], arguments[:9])
        self.assertNotIn("PASSWORD", str(arguments))
        self.runner.side_effect = [b"mon_kado_migrations\n", b"sensitive garbage\n"]
        with self.assertRaisesRegex(DeploymentError, "DATABASE_HISTORY_INVALID"):
            self.runtime.history()
        self.runner.side_effect = [b"\n"]
        self.assertEqual([], self.runtime.history())

    def test_telemetry_must_belong_to_new_process_and_be_private(self):
        path = self.root / "api" / "snapshot.json"
        path.parent.mkdir()
        now = datetime.now(timezone.utc)
        value = {"schemaVersion": 1, "service": "api", "bootId": "a" * 32, "createdAt": now.isoformat()}
        atomic_write(path, json.dumps(value).encode())
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
        atomic_write(self.root / "production.env", b"DATABASE_URL=postgres://mon_kado:synthetic@127.0.0.1:5432/mon_kado?sslmode=disable\n")
        atomic_write(self.root / "deployment-smoke.json", json.dumps(ACCOUNT).encode())
        atomic_write(self.root / "monitoring.json", b'{"frontendEnabled":true}')
        client = Mock(token="token")
        self.clients.return_value = client
        with patch.object(self.runtime, "prepare") as prepare, patch("deploy_runtime.native_artifacts.verify_directory") as verify, \
             patch.object(self.runtime, "container", return_value={"running": True, "oom": False, "id": "a", "monitorId": "a", "image": "b", "restarts": 2}), \
             patch("deploy_runtime.shutil.disk_usage", return_value=Mock(free=3 * 1024 ** 3)), \
             patch("deploy_runtime.FunctionalSmoke") as smoke, \
             patch("deploy_runtime.smoke_technical.frontend", return_value={"revision": "f" * 40}):
            smoke.return_value.journal.exists.return_value = True
            self.runtime.preflight(self.release, self.release)
            prepare.assert_called_once_with(self.release)
            verify.assert_called_once_with(self.runtime.directory(self.release), self.release)
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
        container = {"running": True, "oom": False, "restarts": 0, "startedAt": "now", "id": "a", "monitorId": "a", "image": "b"}
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
            with self.assertRaisesRegex(DeploymentError, "SERVICE_UNSTABLE"):
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
        with patch.object(self.runtime, "technical_sample", side_effect=DeploymentError("SERVICE_UNSTABLE")):
            with self.assertRaisesRegex(DeploymentError, "SERVICE_UNSTABLE"):
                self.runtime.smoke(self.release)
        with patch.object(self.runtime, "technical_sample", side_effect=OSError()):
            with self.assertRaisesRegex(DeploymentError, "TECHNICAL_SMOKE_TIMEOUT"):
                self.runtime.smoke(self.release)

    def test_dead_api_is_terminal_before_executable_identity_or_readiness_probes(self):
        for outcome in ({"running": False, "oom": False}, {"running": False, "oom": True}):
            with self.subTest(outcome=outcome), patch.object(self.runtime, "container", return_value=outcome) as observe, \
                 patch.object(self.runtime, "verify_active") as verify, patch.object(self.runtime, "pause") as pause, \
                 patch("deploy_runtime.smoke_technical.check") as check:
                with self.assertRaisesRegex(DeploymentError, "^SERVICE_UNSTABLE$"):
                    self.runtime.wait_technical(self.release)
                observe.assert_called_once_with("api")
                verify.assert_not_called()
                pause.assert_not_called()
                check.assert_not_called()

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
