"""Read-only probe tests; external HTTP is replaced at the transport boundary."""

import contextlib
import io
import runpy
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock, patch
import urllib.error

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Security"))
import security_probe as probe


class ProbeTests(unittest.TestCase):
    def test_command_entrypoint_returns_failure_without_printing_headers(self):
        # Arrange
        with patch.object(probe.urllib.request, "build_opener") as build:
            response = build.return_value.open.return_value
            response.status = 503
            response.headers = {"Set-Cookie": "private-value"}
            # Act
            with contextlib.redirect_stdout(io.StringIO()) as output, self.assertRaises(SystemExit) as result:
                runpy.run_path(str(Path(probe.__file__)), run_name="__main__")
        # Assert
        self.assertEqual(1, result.exception.code)
        self.assertNotIn("private-value", output.getvalue())

    def test_verify_reports_only_boolean_evidence(self):
        # Arrange
        responses = [(308, {"location": probe.API + "/readiness"}), (200, {
            "strict-transport-security": "max-age=31536000", "content-security-policy": "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
            "x-content-type-options": "nosniff", "x-frame-options": "DENY", "referrer-policy": "no-referrer"}),
            (204, {"access-control-allow-origin": probe.FRONTEND, "access-control-allow-credentials": "true"}), (204, {})]
        send = Mock(side_effect=responses)
        # Act
        results = probe.verify(send)
        # Assert
        self.assertTrue(all(results.values()))
        self.assertEqual(4, send.call_count)
        self.assertTrue(all(call.args[1] in ("HEAD", "OPTIONS") for call in send.call_args_list))
        self.assertFalse(any(probe.verify(lambda *args: (503, {})).values()))

    def test_main_does_not_disclose_transport_exception(self):
        # Arrange / Act / Assert
        with contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(1, probe.main(Mock(side_effect=RuntimeError("private-secret"))))
        self.assertNotIn("private-secret", output.getvalue())
        for expected, value in ((0, True), (1, False)):
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(expected, probe.main(lambda: {"valid": value}))

    def test_fetch_inspects_errors_without_following_redirects(self):
        # Arrange / Act / Assert
        self.assertIsNone(probe.NoRedirect().redirect_request(None, None, 308, None, None, None))
        for fails in (False, True):
            response = urllib.error.HTTPError(probe.API, 308, "redirect", {"Location": probe.API}, io.BytesIO())
            with patch.object(probe.urllib.request, "build_opener") as build:
                if fails:
                    build.return_value.open.side_effect = response
                else:
                    build.return_value.open.return_value = response
                self.assertEqual((308, {"location": probe.API}), probe.fetch(probe.API, "HEAD", {}))
                self.assertEqual(10, build.return_value.open.call_args.kwargs["timeout"])


if __name__ == "__main__":
    unittest.main()
