"""Real local TLS/proxy checks using disposable, loopback-only Docker resources."""

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


def docker(*arguments):
    result = subprocess.run(["docker", *arguments], check=True, capture_output=True, text=True, timeout=120)
    return result.stdout.strip()


def ready(container, message, after=None):
    """Wait for the actual startup event, not an arbitrary delay or test retry."""
    started_at = docker("inspect", "--format", "{{.State.StartedAt}}", container)
    process = subprocess.Popen(["docker", "logs", "--follow", "--since", started_at, container], stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True)

    def read():
        marker_seen = after is None
        for line in process.stdout:
            if not marker_seen:
                marker_seen = after in line
                continue
            if message in line:
                return
        raise AssertionError("Container ended before its readiness event")

    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
        future = executor.submit(read)
        try:
            future.result(timeout=30)
        finally:
            process.terminate()
            process.wait(timeout=10)
            process.stdout.close()


@unittest.skipUnless(os.environ.get("MONKADO_CADDY_TESTS") == "1", "Explicit disposable Docker opt-in required")
class CaddyTests(unittest.TestCase):
    def test_https_proxy_headers_and_unavailable_upstream(self):
        # Arrange
        name = "monkado-security-" + secrets.token_hex(8)
        api = name + "-api"
        edge = name + "-edge"
        root = Path(__file__).resolve().parents[2]
        docker("network", "create", name)
        server = """import http.server,json
class Handler(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  self.send_response(200)
  self.send_header('Content-Security-Policy', 'upstream-policy')
  self.end_headers()
  self.wfile.write(json.dumps(dict(self.headers)).encode())
 def log_message(self,*args): pass
server=http.server.HTTPServer(('0.0.0.0',8080),Handler)
print('fixture-ready',flush=True)
server.serve_forever()
"""
        try:
            docker("run", "-d", "--name", api, "--network", name, "--network-alias", "api",
                   "python:3.13-slim@sha256:8d9d0b8bcf6506481eae4907c18f5e3e7902e629f5f6d684f9e7c32e85e3ddf0",
                   "python", "-u", "-c", server)
            ready(api, "fixture-ready")
            docker("run", "-d", "--name", edge, "--network", name, "-e", "API_HOST=localhost",
                   "-p", "127.0.0.1::443", "-p", "127.0.0.1::80",
                   "--mount", "type=bind,src=" + str(root / "deployments/caddy/Caddyfile") + ",dst=/etc/caddy/Caddyfile,readonly",
                   "caddy:2.11.4-alpine")
            ready(edge, "certificate obtained successfully")
            ports = json.loads(docker("inspect", "--format", "{{json .NetworkSettings.Ports}}", edge))
            https_port = int(ports["443/tcp"][0]["HostPort"])
            http_port = int(ports["80/tcp"][0]["HostPort"])
            with tempfile.TemporaryDirectory(prefix="monkado-security-ca-") as temporary:
                certificate = Path(temporary) / "root.crt"
                docker("cp", edge + ":/data/caddy/pki/authorities/local/root.crt", str(certificate))
                context = ssl.create_default_context(cafile=str(certificate))

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

                docker("stop", api)
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
                self.assertEqual({}, json.loads(docker("inspect", "--format", "{{json .HostConfig.PortBindings}}", api)))
                logs = subprocess.run(["docker", "logs", edge], check=True, capture_output=True, text=True, timeout=30)
                for canary in ("synthetic-query-canary", "synthetic-bearer-canary", "synthetic-cookie-canary"):
                    self.assertNotIn(canary, logs.stdout + logs.stderr)
        finally:
            # Exact generated resource names only; never enumerate or prune other containers.
            for container in (edge, api):
                subprocess.run(["docker", "rm", "-f", "-v", container], capture_output=True, timeout=30)
            docker("network", "rm", name)

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
