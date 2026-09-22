import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from policy import LABEL, PerformanceError
from runtime import Bench, command, private_write, protect_directory, generator_user, keep_awake


class RuntimeTests(unittest.TestCase):
    def test_windows_wake_lock_is_temporary_and_released_on_failure(self):
        with patch("runtime.sys.platform", "win32"), patch("runtime.ctypes.windll", create=True) as native:
            native.kernel32.SetThreadExecutionState.return_value = 1
            with self.assertRaisesRegex(ValueError, "interrupted"):
                with keep_awake():
                    raise ValueError("interrupted")
            self.assertEqual([(0x80000001,), (0x80000000,)],
                             [call.args for call in native.kernel32.SetThreadExecutionState.call_args_list])
            native.kernel32.SetThreadExecutionState.return_value = 0
            with self.assertRaisesRegex(PerformanceError, "WAKE_LOCK_UNAVAILABLE"):
                with keep_awake():
                    self.fail("The campaign must not start without a wake lock")

    def fixture_source(self, root):
        bench = Bench(root, "mk816-012345abcdef")
        bench.source_code.mkdir(parents=True)
        (bench.source_code / "fixture.py").write_text("# fixed")
        (bench.source_code / "scenario.js").write_text("// fixed")
        (bench.source_code / "ignored.txt").write_text("not executable")
        caddy = Path(root) / "deployments" / "caddy" / "Caddyfile"
        caddy.parent.mkdir(parents=True)
        caddy.write_text("{$API_HOST} {\n reverse_proxy api:8080\n}\n")
        return bench

    @staticmethod
    def docker_output(arguments, **kwargs):
        if arguments == ["docker", "network", "ls", "-q"]:
            return "network1\nnetwork2"
        if arguments[:3] == ["docker", "network", "inspect"]:
            return '[{"Subnet":"10.241.0.0/16"},{}]' if arguments[-1] == "network1" else "null"
        if arguments[:3] == ["docker", "image", "inspect"]:
            return "sha256:test"
        if "rev-parse" in arguments:
            return "a" * 40
        return ""

    def test_create_freezes_harness_and_initializes_only_new_owned_resources(self):
        with tempfile.TemporaryDirectory() as root:
            bench = self.fixture_source(root)
            calls = []
            def compose(*args, **kwargs):
                calls.append(args)
                if args[0] == "exec" and sum(item[0] == "exec" for item in calls) == 1:
                    raise PerformanceError("COMMAND_FAILED")
                return "caddy-id"
            with patch.object(bench, "local_only"), patch.object(bench, "group", return_value={"qualified": True}):
                with patch.object(bench, "compose", side_effect=compose), patch.object(bench, "sql") as sql:
                    with patch("runtime.command", side_effect=self.docker_output), patch("runtime.time.sleep"):
                        self.assertTrue(bench.create()["qualified"])
            self.assertIn("current_database()", sql.call_args.args[0])
            self.assertEqual(2, len(list(bench.code.iterdir())))
            self.assertEqual(10, len(list(bench.directory.glob("fixtures-*.json"))))
            metadata = json.loads((bench.directory / "metadata.json").read_text())
            self.assertFalse(metadata["externalProviders"])
            with patch.object(bench, "local_only"), self.assertRaisesRegex(PerformanceError, "RUN_ALREADY_EXISTS"):
                bench.create()

    def test_systemd_creation_configures_only_the_owned_slice(self):
        with tempfile.TemporaryDirectory() as root:
            bench = self.fixture_source(root)
            bench.driver = "systemd"
            with patch.object(bench, "local_only"), patch.object(bench, "group", return_value={}) as group:
                with patch.object(bench, "compose"), patch.object(bench, "sql"), patch.object(bench, "owned_containers") as owned:
                    with patch("runtime.command", side_effect=self.docker_output):
                        bench.create()
            owned.assert_called_once()
            self.assertEqual("mk816012345abcdef.slice", bench.parent)
            self.assertEqual([("configure",), ("status",)], [call.args for call in group.call_args_list])

    def test_create_refuses_subnet_exhaustion_and_readiness_timeout(self):
        for fault in ("subnet", "readiness"):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as root:
                bench = self.fixture_source(root)
                def docker(arguments, **kwargs):
                    if fault == "subnet" and arguments[:3] == ["docker", "network", "inspect"]:
                        return '[{"Subnet":"0.0.0.0/0"}]'
                    return self.docker_output(arguments, **kwargs)
                def compose(*args, **kwargs):
                    if args[0] == "exec":
                        raise PerformanceError("COMMAND_FAILED")
                    return ""
                with patch.object(bench, "local_only"), patch.object(bench, "group"), patch.object(bench, "sql"):
                    with patch.object(bench, "compose", side_effect=compose), patch("runtime.command", side_effect=docker):
                        with patch("runtime.time.monotonic", side_effect=[0, 121]):
                            expected = "NO_PRIVATE_SUBNET" if fault == "subnet" else "READINESS_TIMEOUT"
                            with self.assertRaisesRegex(PerformanceError, expected):
                                bench.create()
        with tempfile.TemporaryDirectory() as root:
            bench = self.fixture_source(root)
            with patch.object(bench, "local_only"), patch("runtime.command", return_value="collision"):
                with self.assertRaisesRegex(PerformanceError, "DOCKER_RESOURCES_ALREADY_EXIST"):
                    bench.create()

    def test_final_invariants_include_dataset_and_reservation_capacity(self):
        bench = Bench(".", "mk816-012345abcdef")
        result = {"members": 100, "lists": 500, "wishes": 10000, "images": 200, "overReserved": 0}
        with patch.object(bench, "sql", return_value=json.dumps(result)):
            self.assertEqual(result, bench.verify_data())
        for key in result:
            changed = {**result, key: 1}
            with patch.object(bench, "sql", return_value=json.dumps(changed)):
                with self.assertRaisesRegex(PerformanceError, "DATA_INVARIANT_FAILED"):
                    bench.verify_data()

    def test_command_captures_output_and_never_echoes_credentials(self):
        result = subprocess.CompletedProcess([], 0, " ok \n", "secret")
        with patch("runtime.subprocess.run", return_value=result) as execute:
            self.assertEqual("ok", command(["docker", "version"], data="private"))
            self.assertEqual("ok \nsecret", command(["docker", "logs"], include_stderr=True))
            self.assertTrue(execute.call_args.kwargs["capture_output"])
        for error in (OSError("secret"), subprocess.TimeoutExpired("secret", 1)):
            with patch("runtime.subprocess.run", side_effect=error), self.assertRaisesRegex(PerformanceError, "^COMMAND_UNAVAILABLE$"):
                command(["docker"])
        with patch("runtime.subprocess.run", return_value=subprocess.CompletedProcess([], 1, "secret", "secret")):
            with self.assertRaisesRegex(PerformanceError, "^COMMAND_FAILED$"):
                command(["docker"])

    def test_private_files_cannot_overwrite_existing_material(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "private" / "example"
            private_write(path, "synthetic")
            self.assertEqual("synthetic", path.read_text())
            with self.assertRaises(FileExistsError):
                private_write(path, "replace")

    def test_windows_acl_grants_only_current_owner_and_system(self):
        with patch("runtime.sys.platform", "win32"):
            self.assertEqual("0", generator_user())
            with patch("runtime.command", side_effect=['"fixture-user","S-1-5-21-123"', ""]) as execute:
                protect_directory(Path("private-run"))
                self.assertIn("*S-1-5-21-123:(OI)(CI)F", execute.call_args.args[0])
                self.assertIn("/inheritance:r", execute.call_args.args[0])
            with patch("runtime.command", return_value='"fixture-user","unknown"'):
                with self.assertRaisesRegex(PerformanceError, "OWNER_ID_UNAVAILABLE"):
                    protect_directory(Path("private-run"))

    def test_remote_docker_and_unsupported_cgroup_are_refused(self):
        bench = Bench(".", "mk816-012345abcdef")
        for endpoint in ("ssh://server", "tcp://server:2375", "npipe:////remote-server/pipe/docker_engine", "unix://remote/path"):
            with patch("runtime.command", return_value=endpoint), self.assertRaisesRegex(PerformanceError, "REMOTE_DOCKER"):
                bench.local_only()
        with patch("runtime.command", side_effect=["unix:///var/run/docker.sock", "linux/1"]):
            with self.assertRaisesRegex(PerformanceError, "LINUX_CGROUP"):
                bench.local_only()
        with patch.dict("os.environ", {"DOCKER_HOST": "ssh://remote"}):
            with patch("runtime.command", side_effect=["unix:///var/run/docker.sock", "linux/2"]):
                with self.assertRaisesRegex(PerformanceError, "DOCKER_OVERRIDE"):
                    bench.local_only()
        with patch.dict("os.environ", {"DOCKER_HOST": "", "DOCKER_CONTEXT": ""}):
            with patch("runtime.command", side_effect=["npipe:////./pipe/docker_engine", "linux/2", "cgroupfs"]):
                bench.local_only()

    def test_owned_containers_reject_any_other_label(self):
        bench = Bench(".", "mk816-012345abcdef")
        with patch.object(bench, "compose", return_value="id1\nid2"), patch("runtime.command", return_value=bench.identifier):
            self.assertEqual(["id1", "id2"], bench.owned_containers())
        with patch.object(bench, "compose", return_value="foreign"), patch("runtime.command", return_value="mon-kado"):
            with self.assertRaisesRegex(PerformanceError, "OWNERSHIP"):
                bench.owned_containers()
        with patch.object(bench, "owned_containers"), patch.object(bench, "compose", return_value="42") as compose:
            self.assertEqual("42", bench.sql("SELECT 42;"))
            self.assertEqual("SELECT 42;", compose.call_args.kwargs["data"])

    def test_compose_never_sources_environment_as_shell(self):
        bench = Bench(".", "mk816-012345abcdef")
        with patch("runtime.command", return_value="done") as run:
            self.assertEqual("done", bench.compose("ps", data="secret"))
            self.assertEqual("secret", run.call_args.kwargs["data"])
            self.assertEqual(["docker", "compose", "-p", bench.identifier], run.call_args.args[0][:4])

    def test_cgroup_status_is_read_only_and_changes_are_explicit(self):
        bench = Bench(".", "mk816-012345abcdef")
        for action in ("status", "create", "remove"):
            with patch("runtime.command", return_value='{"qualified":true}') as run:
                self.assertTrue(bench.group(action)["qualified"])
                self.assertEqual(action != "status", "--privileged" in run.call_args.args[0])

    def test_health_verifies_parent_and_published_ports(self):
        bench = Bench(".", "mk816-012345abcdef")
        state = {"image": "sha256:test", "state": "running", "oom": False, "restarts": 0, "parent": bench.identifier, "ports": {}}
        with patch.object(bench, "group", return_value={"qualified": True, "oom": 0}), patch.object(bench, "compose", return_value="owned"):
            with patch("runtime.command", return_value=json.dumps(state)):
                self.assertTrue(bench.inspect_health()["alive"])
                with patch.object(bench, "compose", side_effect=["id", "id", "id", "id", PerformanceError("DOWN")]):
                    self.assertFalse(bench.inspect_health()["alive"])
            for changes in ({"parent": "other"}, {"ports": {"80": []}}):
                with patch("runtime.command", return_value=json.dumps({**state, **changes})):
                    with self.assertRaisesRegex(PerformanceError, "ISOLATION"):
                        bench.inspect_health()
        with patch.object(bench, "group", return_value={}), patch.object(bench, "compose", return_value=""):
            with self.assertRaisesRegex(PerformanceError, "CONTAINER_MISSING"):
                bench.inspect_health()

    def test_generators_have_no_host_network_or_capabilities(self):
        with tempfile.TemporaryDirectory() as root:
            bench = self.fixture_source(root)
            bench.directory.mkdir(parents=True)
            with patch("runtime.subprocess.Popen") as process:
                self.assertEqual(10, len(bench.generators("smoke")))
            for call in process.call_args_list:
                args = call.args[0]
                self.assertIn(bench.identifier + "_edge", args)
                self.assertNotIn("--privileged", args)
                self.assertNotIn("host", args)
                self.assertIn("--no-usage-report", args)

    def test_cleanup_targets_only_labelled_resources_and_refuses_mismatch(self):
        bench = Bench(".", "mk816-012345abcdef")
        def execute(arguments, **kwargs):
            if "ls" in arguments:
                return "owned"
            if "inspect" in arguments:
                return json.dumps({LABEL: bench.identifier})
            return ""
        with patch.object(bench, "local_only"), patch.object(bench, "owned_containers"), patch.object(bench, "group") as group:
            with patch("runtime.command", side_effect=execute) as run:
                bench.cleanup()
                removes = [call.args[0] for call in run.call_args_list if "rm" in call.args[0]]
                self.assertEqual(3, len(removes))
                self.assertTrue(all(arguments[-1] == "owned" for arguments in removes))
                self.assertFalse(any("prune" in arguments for arguments in removes))
            group.assert_called_once_with("remove")
            with patch("runtime.command", side_effect=["foreign", json.dumps({LABEL: "other"})]):
                with self.assertRaisesRegex(PerformanceError, "OWNERSHIP_MISMATCH"):
                    bench.cleanup()

    def test_failure_diagnostics_strip_all_log_content_except_known_categories(self):
        bench = Bench(".", "mk816-012345abcdef")
        self.assertEqual({}, bench.failure_diagnostics())
        bench.cg_created = True
        with patch.object(bench, "compose", return_value="owned"):
            with patch("runtime.command", side_effect=['{"running":false,"oom":true,"exitCode":137}', "private-value OutOfMemoryException"] * 4):
                report = bench.failure_diagnostics()
                self.assertNotIn("private-value", json.dumps(report))
                self.assertEqual(["OutOfMemoryException"], report["api"]["categories"])
            with patch("runtime.command", side_effect=PerformanceError("COMMAND_FAILED")):
                self.assertTrue(bench.failure_diagnostics()["api"]["diagnosticsUnavailable"])
