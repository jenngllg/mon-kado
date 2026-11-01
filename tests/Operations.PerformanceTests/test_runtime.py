import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from policy import PerformanceError
from runtime import Bench, command, private_write, protect_directory, keep_awake


class RuntimeTests(unittest.TestCase):
    def test_windows_wake_lock_is_temporary_and_released_on_failure(self):
        with patch("native_runtime.sys.platform", "win32"), patch("native_runtime.ctypes.windll", create=True) as native:
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
        binary = subprocess.CompletedProcess([], 0, b"ok", b"")
        with patch("native_runtime.subprocess.run", return_value=binary) as execute:
            self.assertEqual("ok", command(["psql"], data=b"archive"))
            self.assertFalse(execute.call_args.kwargs["text"])
        result = subprocess.CompletedProcess([], 0, " ok \n", "secret")
        with patch("native_runtime.subprocess.run", return_value=result) as execute:
            self.assertEqual("ok", command(["caddy", "version"], data="private"))
            self.assertEqual("ok \nsecret", command(["systemctl", "show"], include_stderr=True))
            self.assertTrue(execute.call_args.kwargs["capture_output"])
        for error in (OSError("secret"), subprocess.TimeoutExpired("secret", 1)):
            with patch("native_runtime.subprocess.run", side_effect=error), self.assertRaisesRegex(PerformanceError, "^COMMAND_UNAVAILABLE$"):
                command(["psql"])
        with patch("native_runtime.subprocess.run", return_value=subprocess.CompletedProcess([], 1, "secret", "secret")):
            with self.assertRaisesRegex(PerformanceError, "^COMMAND_FAILED$"):
                command(["psql"])


    def test_private_files_cannot_overwrite_existing_material(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "private" / "example"
            private_write(path, "synthetic")
            self.assertEqual("synthetic", path.read_text())
            with self.assertRaises(FileExistsError):
                private_write(path, "replace")


    def test_windows_acl_grants_only_current_owner_and_system(self):
        with patch("native_runtime.sys.platform", "win32"):
            with patch("native_runtime.command", side_effect=['"fixture-user","S-1-5-21-123"', ""]) as execute:
                protect_directory(Path("private-run"))
                self.assertIn("*S-1-5-21-123:(OI)(CI)F", execute.call_args.args[0])
                self.assertIn("/inheritance:r", execute.call_args.args[0])
            with patch("native_runtime.command", return_value='"fixture-user","unknown"'):
                with self.assertRaisesRegex(PerformanceError, "OWNER_ID_UNAVAILABLE"):
                    protect_directory(Path("private-run"))


    def test_preflight_refuses_unprivileged_or_non_linux_hosts_before_any_effect(self):
        bench = Bench('.', 'mk816-012345abcdef')
        for platform, uid in [('win32',0), ('linux',1000)]:
            with self.subTest(platform=platform,uid=uid), patch('native_runtime.sys.platform',platform), patch('native_runtime.os.geteuid',return_value=uid):
                with patch('native_runtime.command') as execute, self.assertRaisesRegex(PerformanceError,'NATIVE_LINUX_ROOT_REQUIRED'):
                    bench.local_only()
                execute.assert_not_called()

    def test_service_start_uses_owned_namespace_private_environment_and_shared_slice(self):
        bench = Bench('.', 'mk816-012345abcdef')
        bench.directory = Path('/tmp') / bench.identifier
        with patch('native_runtime.command') as execute:
            bench.start('api',['/tmp/owned/api'],environment=bench.directory/'api.env')
        args = execute.call_args.args[0]
        self.assertEqual('systemd-run',args[0])
        for value in ['--slice=mk816012345abcdef.slice','--unit=mk816-012345abcdef-api.service',
                      '--property=NetworkNamespacePath=/run/netns/mk816-012345abcdef',
                      '--property=User=nobody','--property=Restart=no','--property=NoNewPrivileges=yes',
                      '--property=ProtectSystem=strict','--property=UMask=0077',
                      '--property=EnvironmentFile=/tmp/mk816-012345abcdef/api.env']:
            self.assertIn(value,args)
        self.assertNotIn('sh',args)
        with self.assertRaisesRegex(PerformanceError,'INVALID_SERVICE'):
            bench.start('production',['anything'])

    def test_preparation_uses_ten_distinct_loopback_addresses_serially_without_retry(self):
        bench = Bench('.', 'mk816-012345abcdef')
        with patch.object(bench,'namespace') as execute:
            bench.prepare_images()
        self.assertEqual(10,execute.call_count)
        for index, call in enumerate(execute.call_args_list):
            self.assertIn('MONKADO_PERFORMANCE_SOURCE_IP=127.0.0.'+str(200+index),call.args)
            self.assertEqual(('prepare',str(index)),call.args[-2:])
        with patch.object(bench,'namespace',side_effect=PerformanceError('COMMAND_FAILED')) as execute:
            with self.assertRaisesRegex(PerformanceError,'COMMAND_FAILED'):
                bench.prepare_images()
            execute.assert_called_once()

    def test_sql_checks_ownership_before_local_namespace_access(self):
        bench = Bench('.', 'mk816-012345abcdef')
        with patch.object(bench,'owned') as owned, patch.object(bench,'namespace',return_value='42') as execute:
            self.assertEqual('42',bench.sql('SELECT 42;'))
        owned.assert_called_once_with()
        self.assertIn('127.0.0.1',execute.call_args.args)
        self.assertEqual('SELECT 42;',execute.call_args.kwargs['data'])
        with patch.object(bench,'owned',side_effect=PerformanceError('OWNERSHIP_MISMATCH')):
            with patch.object(bench,'namespace') as execute, self.assertRaisesRegex(PerformanceError,'OWNERSHIP_MISMATCH'):
                bench.sql('SELECT 42;')
            execute.assert_not_called()

    def test_observer_keeps_actual_oom_and_restart_and_refuses_foreign_groups(self):
        bench = Bench('.', 'mk816-012345abcdef')
        state = 'ActiveState=active\nNRestarts=0\nResult=success\nControlGroup=/'+bench.parent+'/owned.service'
        with patch.object(bench,'owned'), patch.object(bench,'group',return_value={'qualified':True,'oom':0}):
            with patch('native_runtime.command',return_value=state):
                self.assertTrue(bench.inspect_health()['alive'])
            with patch('native_runtime.command',return_value=state.replace('NRestarts=0','NRestarts=2').replace('Result=success','Result=oom-kill').replace('ActiveState=active','ActiveState=failed')):
                health = bench.inspect_health()
                self.assertFalse(health['alive'])
                self.assertEqual(1,health['oom'])
                self.assertEqual(8,health['restarts'])
            with patch('native_runtime.command',return_value=state.replace(bench.parent,'foreign.slice')):
                with self.assertRaisesRegex(PerformanceError,'ISOLATION_MISMATCH'):
                    bench.inspect_health()

    def test_cleanup_refuses_unowned_resources_before_stopping_any_service(self):
        bench = Bench('.', 'mk816-012345abcdef')
        with patch.object(bench,'local_only'), patch.object(bench,'owned',side_effect=PerformanceError('OWNERSHIP_MISMATCH')):
            with patch('native_runtime.command') as execute, self.assertRaisesRegex(PerformanceError,'OWNERSHIP_MISMATCH'):
                bench.cleanup()
            execute.assert_not_called()

    def test_process_stop_is_bounded_and_kills_only_pending_generators(self):
        bench = Bench('.', 'mk816-012345abcdef')
        exited, running = Mock(),Mock()
        exited.poll.return_value=0
        running.poll.return_value=None
        running.wait.side_effect=[subprocess.TimeoutExpired('fixture',20),0]
        bench.stop_processes([exited,running],None)
        exited.terminate.assert_not_called()
        running.terminate.assert_called_once()
        running.kill.assert_called_once()
        self.assertEqual([20,5],[call.kwargs['timeout'] for call in running.wait.call_args_list])

    def test_partial_cleanup_skips_missing_units_and_refuses_foreign_units_before_any_stop(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            bench = Bench('.', 'mk816-012345abcdef')

            def mapped(value, *parts):
                return root / str(value).lstrip('/') / Path(*parts)

            for foreign in (False, True):
                with self.subTest(foreign=foreign):
                    def result(arguments, **_):
                        if arguments[:2] == ['systemctl','show']:
                            return ('LoadState=loaded\nControlGroup=/foreign.slice/service\nFragmentPath=/etc/systemd/system/service' if foreign
                                    else 'LoadState=not-found\nControlGroup=\nFragmentPath=')
                        return ''

                    with patch.object(bench,'local_only'), patch.object(bench,'owned'), patch('native_runtime.Path',side_effect=mapped):
                        with patch('native_runtime.command',side_effect=result) as execute:
                            if foreign:
                                with self.assertRaisesRegex(PerformanceError,'OWNERSHIP_MISMATCH'):
                                    bench.cleanup()
                            else:
                                bench.cleanup()
                            self.assertFalse(any(call.args[0][:2] == ['systemctl','stop'] for call in execute.call_args_list))
