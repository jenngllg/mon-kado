"""Real native TLS/proxy checks in an explicitly isolated Linux network namespace."""

import concurrent.futures
import http.client
import json
import os
from pathlib import Path
import secrets
import ssl
import subprocess
import tempfile
import unittest


from native_fixture import NativeCaddy, stop


@unittest.skipUnless(os.environ.get("MONKADO_CADDY_TESTS") == "1", "Explicit isolated native opt-in required")
class CaddyTests(unittest.TestCase):
    def test_https_proxy_headers_and_unavailable_upstream(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
        server = """import http.server,json
class Handler(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  self.send_response(200)
  self.send_header('Content-Security-Policy', 'upstream-policy')
  self.end_headers()
  self.wfile.write(json.dumps(dict(self.headers)).encode())
 def log_message(self,*args): pass
server=http.server.HTTPServer(('127.0.0.1',7000),Handler)
print('fixture-ready',flush=True)
server.serve_forever()
"""
        configuration = (root / 'deployments/caddy/Caddyfile').read_text().replace(
            'import /opt/monkado/deployments/caddy/frontend/*.caddy', '')
        with NativeCaddy(root, configuration, {'API_HOST':'localhost'}, server) as fixture:
            context = ssl.create_default_context(cafile=str(fixture.certificate))
            https_port, http_port = 443, 80

            # Act / Assert: real certificate verification and unspoofable forwarded headers.
            connection = http.client.HTTPSConnection("localhost", https_port, context=context, timeout=10)
            connection.request("GET", "/probe", headers={"Host": "localhost", "X-Forwarded-For": "203.0.113.99",
                                                        "X-Forwarded-Proto": "http"})
            response = connection.getresponse()
            forwarded = {key.lower(): value for key, value in json.loads(response.read()).items()}
            self.assertEqual(200, response.status)
            self.assertNotIn("203.0.113.99", forwarded["x-forwarded-for"])
            self.assertEqual("https", forwarded["x-forwarded-proto"])
            self.assert_headers(response)
            connection.close()

            connection = http.client.HTTPConnection("localhost", http_port, timeout=10)
            connection.request("GET", "/probe", headers={"Host": "localhost"})
            response = connection.getresponse()
            response.read()
            self.assertEqual(308, response.status)
            self.assertEqual("https://localhost/probe", response.getheader("Location"))
            self.assertIsNone(response.getheader("Strict-Transport-Security"))
            connection.close()

            connection = http.client.HTTPConnection("localhost", http_port, timeout=10)
            connection.request("GET", "/probe", headers={"Host": "untrusted.invalid"})
            response = connection.getresponse()
            response.read()
            self.assertNotIn(response.status, (301, 302, 303, 307, 308))
            self.assertIsNone(response.getheader("Location"))
            connection.close()

            connection = http.client.HTTPSConnection("localhost", https_port, context=context, timeout=10)
            connection.request("GET", "/probe", headers={"Host": "untrusted.invalid"})
            response = connection.getresponse()
            response.read()
            self.assertIn(response.status, (404, 421))
            connection.close()

            stop(fixture.upstream)
            connection = http.client.HTTPSConnection("localhost", https_port, context=context, timeout=10)
            connection.request("GET", "/probe?token=synthetic-query-canary",
                               headers={"Host": "localhost", "Authorization": "Bearer synthetic-bearer-canary",
                                        "Cookie": "refresh=synthetic-cookie-canary"})
            response = connection.getresponse()
            response.read()
            self.assertEqual(502, response.status)
            self.assertEqual("no-store", response.getheader("Cache-Control"))
            self.assert_headers(response)
            connection.close()
            stop(fixture.edge)
            logs = fixture.edge.stdout.read()
            for canary in ("synthetic-query-canary", "synthetic-bearer-canary", "synthetic-cookie-canary"):
                self.assertNotIn(canary, logs)

    def assert_headers(self, response):
        expected = {
            "Content-Security-Policy": "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
            "Strict-Transport-Security": "max-age=31536000",
            "X-Content-Type-Options": "nosniff", "X-Frame-Options": "DENY", "Referrer-Policy": "no-referrer",
        }
        for header, value in expected.items():
            self.assertEqual([value], [content for key, content in response.getheaders() if key.lower() == header.lower()])
        self.assertIsNone(response.getheader("Server"))


if __name__ == "__main__":
    unittest.main()
