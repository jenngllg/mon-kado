"""HTTP body, header, certificate and deadline checks using synthetic responses only."""

import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Frontend"))
import frontend_contract as contract
import frontend_probe as probe


def security_headers():
    """Mirror the reviewed Caddy response without including member data."""
    return {"content-type": "text/html; charset=utf-8", "x-content-type-options": "nosniff",
            "x-frame-options": "DENY", "referrer-policy": "no-referrer", "strict-transport-security": "max-age=31536000",
            "cache-control": "no-store", "permissions-policy": "camera=(), microphone=(), geolocation=(), payment=(), usb=(), clipboard-write=(self)",
            "content-security-policy": "default-src 'none'; script-src 'self'; base-uri 'none'; object-src 'none'; "
            "frame-ancestors 'none'; form-action 'self'; connect-src 'self' https://api.monkado.fr;"}


class ProbeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "assets").mkdir()
        self.revision = "a" * 40
        self.marker = contract.release_marker(self.revision, True)
        (self.root / "release.json").write_text(json.dumps(self.marker))
        for name in ("index.html", *contract.LEGAL_PAGES):
            (self.root / name).write_text("<html lang=fr>synthetic</html>")
        (self.root / "index.html").write_text('<html><script src="/assets/main-abcdefgh.js"></script>'
                                            '<link rel="stylesheet" href="/assets/main-abcdefgh.css">'
                                            '<link rel="modulepreload" href="/assets/main-abcdefgh.js"></html>')
        (self.root / "assets/main-abcdefgh.js").write_text("// synthetic")
        (self.root / "assets/main-abcdefgh.css").write_text("body{}")
        (self.root / "assets/font-abcdefgh.woff2").write_bytes(b"synthetic")
        self.transform = lambda url, values, body: (values, body)
        self.runner = Mock(side_effect=self.respond)

    def respond(self, arguments, **options):
        """Serve exact fixture bytes through the same bounded subprocess output interface."""
        url = arguments[-1]
        values = security_headers()
        if url.endswith("/readiness"):
            values["content-type"] = "text/plain"
            body = b"Healthy"
        else:
            route = url.removeprefix(probe.FRONTEND + "/")
            name = route or "index.html"
            if name + ".html" in contract.LEGAL_PAGES:
                name += ".html"
            values["content-type"] = sorted(probe.CONTENT_TYPES[Path(name).suffix])[0]
            if name.startswith("assets/"):
                values["cache-control"] = "public, max-age=31536000, immutable"
            body = (self.root / name).read_bytes()
        values, body = self.transform(url, values, body)
        Path(arguments[arguments.index("--dump-header") + 1]).write_bytes(
            ("HTTP/2 200\r\n" + "\r\n".join(key + ": " + value for key, value in values.items()) + "\r\n\r\n").encode())
        Path(arguments[arguments.index("--output") + 1]).write_bytes(body)

    def test_checks_every_document_assets_readiness_and_exact_google_marker(self):
        # Arrange / Act
        probe.probe(self.revision, self.root, True, runner=self.runner)
        # Assert
        self.assertEqual(8, self.runner.call_count)
        for call in self.runner.call_args_list:
            arguments = call.args[0]
            self.assertIn("www.monkado.fr:443:127.0.0.1", arguments)
            self.assertIn("--noproxy", arguments)
            self.assertNotIn("--insecure", arguments)
            self.assertNotIn("--location", arguments)
            self.assertTrue(call.kwargs["check"])
        with self.assertRaises(ValueError):
            probe.probe(self.revision, self.root, False, runner=self.runner)

    def test_wrong_content_readiness_and_timeout_fail(self):
        # Arrange / Act / Assert
        for fragment in ("main-abcdefgh.js", "privacy-policy", "readiness"):
            self.transform = lambda url, values, body: (values, b"wrong") if fragment in url else (values, body)
            with self.subTest(fragment=fragment), self.assertRaises(ValueError):
                probe.probe(self.revision, self.root, True, runner=self.runner)
        with self.assertRaises(ValueError):
            probe.probe(self.revision, self.root, runner=self.runner, clock=Mock(side_effect=[0, 121]))
        (self.root / "assets/main-abcdefgh.js").unlink()
        with self.assertRaises(ValueError):
            probe.probe(self.revision, self.root, runner=self.runner)

    def test_security_headers_reject_fallbacks_redirects_and_contradictions(self):
        # Arrange
        good = security_headers()
        cases = [{"content-type": "application/json"}, {"cache-control": "public"}, {"server": "caddy"},
                 {"set-cookie": "private-canary"}, {"location": "/login"}, {"x-frame-options": "SAMEORIGIN"},
                 {"permissions-policy": ""}, {"content-security-policy": "default-src 'none'; default-src *"},
                 {"content-security-policy": "default-src *"}]
        # Act / Assert
        probe.validate_headers(good, ".html")
        for change in cases:
            with self.subTest(change=change), self.assertRaises(ValueError):
                probe.validate_headers(good | change, ".html")
        self.assertEqual({"content-type": "text/plain"}, probe.headers(b"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\n"))
        for raw in (b"", b"HTTP/2", b"HTTP/1.1 302 Found", b"HTTP/1.1 200 OK\r\ninvalid", b"HTTP/2 200\r\nX-Test: 1\r\nx-test: 2"):
            with self.assertRaises(ValueError):
                probe.headers(raw)

    def test_body_and_headers_are_bounded(self):
        # Arrange
        self.transform = lambda url, values, body: (values, b"x" * (probe.MAX_BODY + 1))
        # Act / Assert
        with self.assertRaises(ValueError):
            probe.probe(self.revision, self.root, runner=self.runner)
        self.runner.reset_mock()
        (self.root / "index.html").write_bytes(b"x" * (probe.MAX_BODY + 1))
        with self.assertRaises(ValueError):
            probe.probe(self.revision, self.root, runner=self.runner)
        self.runner.assert_not_called()

    def test_missing_external_inline_and_wrong_type_entrypoints_fail(self):
        # Arrange / Act / Assert
        for html in ('<html>no executable entrypoint</html>', '<script></script>',
                     '<script src="https://external.invalid/main.js"></script>',
                     '<script src="/assets/missing.js"></script>', '<script src="/assets/../main.js"></script>',
                     '<script src="/assets/main-abcdefgh.js"></script><link rel="stylesheet" href="/assets/main-abcdefgh.js">',
                     '<script src></script>'):
            (self.root / "index.html").write_text(html)
            with self.subTest(html=html), self.assertRaises(ValueError):
                probe.probe(self.revision, self.root, True, runner=self.runner)
