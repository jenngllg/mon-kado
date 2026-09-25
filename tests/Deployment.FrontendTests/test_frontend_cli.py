"""Public command and HTTP boundary checks with no network or production credentials."""

import contextlib
import io
import json
from pathlib import Path
import runpy
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Frontend"))
import frontend_cli as cli
import frontend_runtime as runtime


class CliTests(unittest.TestCase):
    def test_packaging_and_entrypoint_use_only_explicit_public_arguments(self):
        # Arrange
        arguments = ["package", "--dist", "build", "--output", "output", "--revision", "a" * 40,
                     "--backend-revision", "b" * 40, "--configuration-hash", "c" * 64]
        # Act / Assert
        with patch.object(cli.contract, "package_build") as package, contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, cli.main(arguments))
            package.assert_called_once_with(Path("build"), Path("output"), "a" * 40, "b" * 40, "c" * 64)
            with patch.object(sys, "argv", ["frontend_cli.py", *arguments]), self.assertRaises(SystemExit) as exited:
                runpy.run_path(cli.__file__, run_name="__main__")
            self.assertEqual(0, exited.exception.code)

    def test_version_two_packaging_passes_explicit_boolean(self):
        # Arrange
        arguments = ["package", "--dist", "build", "--output", "output", "--revision", "a" * 40,
                     "--backend-revision", "b" * 40, "--configuration-hash", "c" * 64]
        # Act / Assert
        for value in ("true", "false"):
            with patch.object(cli.contract, "package_build") as package, contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, cli.main(arguments + ["--google-enabled", value]))
                package.assert_called_once_with(Path("build"), Path("output"), "a" * 40, "b" * 40, "c" * 64,
                                                google_enabled=value == "true")

    def test_privileged_deploy_and_explicit_rollback(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            state = Path(directory)
            (state / "manifests").mkdir()
            (state / "manifests" / ("a" * 40 + ".json")).write_text('{"fixture":true}')
            with patch.object(cli, "STATE", state), patch.object(cli.os, "geteuid", return_value=0), \
                 patch.object(cli.subprocess, "run", return_value=Mock(stdout="c" * 64 + "\n")), \
                 patch.object(runtime, "Deployment") as deployment, contextlib.redirect_stdout(io.StringIO()):
                deployment.return_value.deploy.return_value = "installed"
                # Act / Assert
                self.assertEqual(0, cli.main(["deploy"]))
                deployment.return_value.deploy.assert_called_with(retry=False)
                self.assertEqual(0, cli.main(["deploy", "--retry"]))
                deployment.return_value.deploy.assert_called_with(retry=True)
                self.assertEqual(0, cli.main(["rollback", "--revision", "a" * 40]))
                deployment.return_value.deploy.assert_called_with(approved={"fixture": True}, retry=True)

    def test_status_is_bounded_and_failures_are_sanitized(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            state = Path(directory)
            (state / "releases").mkdir()
            with patch.object(cli, "STATE", state), patch.object(cli.os, "geteuid", return_value=0):
                # Act / Assert
                for status in (None, {"state": "healthy"}, {"state": "private-canary"}):
                    if status is not None:
                        (state / "status.json").write_text(json.dumps(status))
                    with contextlib.redirect_stdout(io.StringIO()) as output:
                        self.assertEqual(0, cli.main(["status"]))
                    self.assertNotIn("private-canary", output.getvalue())
                with patch.object(cli.subprocess, "run", side_effect=RuntimeError("private-canary")), \
                     contextlib.redirect_stderr(io.StringIO()) as output:
                    self.assertEqual(1, cli.main(["deploy"]))
                self.assertEqual('{"error":"FRONTEND_OPERATION_FAILED"}\n', output.getvalue())
            with patch.object(cli.os, "geteuid", return_value=1000), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(1, cli.main(["deploy"]))


class TransportTests(unittest.TestCase):
    def test_redirects_are_https_only_and_provider_scoped(self):
        # Arrange / Act / Assert
        for url in ("http://github.com/file", "https://evil.invalid/file", "https://user@github.com/file", "https://github.com:8443/file"):
            with self.assertRaises(ValueError):
                runtime.validate_url(url)
        request = urllib.request.Request("https://github.com/file")
        redirected = runtime.SafeRedirect().redirect_request(request, None, 302, "Found", {},
                                                            "https://release-assets.githubusercontent.com/file")
        self.assertEqual("https://release-assets.githubusercontent.com/file", redirected.full_url)

    def test_download_streams_with_bound_and_never_overwrites(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory, patch.object(runtime.urllib.request, "build_opener") as build, \
                patch.object(runtime.urllib.request, "HTTPSHandler", wraps=runtime.urllib.request.HTTPSHandler) as tls:
            response = build.return_value.open.return_value.__enter__.return_value
            target = Path(directory) / "download"
            response.read.side_effect = [b"synthetic", b""]
            # Act
            runtime.download("https://github.com/file", target, 20)
            # Assert
            self.assertEqual(b"synthetic", target.read_bytes())
            context = tls.call_args.kwargs["context"]
            self.assertEqual(runtime.ssl.CERT_REQUIRED, context.verify_mode)
            self.assertTrue(context.check_hostname)
            self.assertGreaterEqual(context.minimum_version, runtime.ssl.TLSVersion.TLSv1_2)
            with self.assertRaises(FileExistsError):
                runtime.download("https://github.com/file", target, 20)
            target.unlink()
            response.read.side_effect = [b"too-long"]
            with self.assertRaises(ValueError):
                runtime.download("https://github.com/file", target, 1)
            self.assertEqual(30, build.return_value.open.call_args.kwargs["timeout"])

    def test_smoke_uses_verified_tls_loopback_and_checks_exact_revision(self):
        # Arrange
        marker = {"revision": "a" * 40, "apiOrigin": "https://api.monkado.fr", "googleEnabled": False}
        with patch.object(runtime.subprocess, "run", return_value=Mock(stdout=json.dumps(marker).encode())) as run:
            # Act
            runtime.probe("a" * 40, ["assets/main-abcdefgh.js"])
            # Assert
            self.assertEqual(4, run.call_count)
            for call in run.call_args_list:
                self.assertIn("www.monkado.fr:443:127.0.0.1", call.args[0])
                self.assertNotIn("--insecure", call.args[0])
                self.assertTrue(call.kwargs["check"])
            run.return_value.stdout = b"{}"
            with self.assertRaises(ValueError):
                runtime.probe("a" * 40, [])

    def test_smoke_requires_the_approved_google_value(self):
        # Arrange
        marker = {"revision": "a" * 40, "apiOrigin": "https://api.monkado.fr", "googleEnabled": True}
        with patch.object(runtime.subprocess, "run", return_value=Mock(stdout=json.dumps(marker).encode())):
            # Act / Assert
            runtime.probe("a" * 40, [], True)
            with self.assertRaises(ValueError):
                runtime.probe("a" * 40, [], False)


if __name__ == "__main__":
    unittest.main()
