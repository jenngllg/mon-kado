"""Real TLS and static hosting checks in explicitly opted-in disposable Docker resources."""

import http.client
import json
import os
from pathlib import Path
import secrets
import socket
import ssl
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Deployment.SecurityTests"))
from test_caddy import docker, ready


@unittest.skipUnless(os.environ.get("MONKADO_FRONTEND_DOCKER_TESTS") == "1", "Explicit isolated Docker opt-in required")
class FrontendHttpTests(unittest.TestCase):
    def test_static_site_and_api_share_caddy_without_exposing_internal_ports(self):
        # Arrange
        name = "monkado-frontend-http-" + secrets.token_hex(8)
        api, edge = name + "-api", name + "-edge"
        root = Path(__file__).resolve().parents[2]
        docker("network", "create", name)
        with tempfile.TemporaryDirectory(prefix="monkado-frontend-http-") as directory:
            fixture = Path(directory)
            current = fixture / "current"
            (current / "assets").mkdir(parents=True)
            (current / "index.html").write_text("<!doctype html><html lang=fr><title>MonKado fixture</title><body>fixture-app</body></html>")
            (current / "assets/main-abcdefgh.js").write_text("// synthetic fixture")
            (current / "release.json").write_text('{"revision":"fixture"}')
            for page in ("legal-notice", "privacy-policy", "terms-of-use"):
                (current / (page + ".html")).write_text("<h1>" + page + "</h1>")
            server = """import http.server
class Handler(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  self.send_response(200)
  self.end_headers()
  self.wfile.write(b'api-fixture')
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
                docker("run", "-d", "--name", edge, "--network", name,
                       "-e", "API_HOST=api.localhost", "-e", "FRONTEND_HOST=www.localhost",
                       "-e", "FRONTEND_APEX_HOST=localhost", "-e", "FRONTEND_API_ORIGIN=https://api.localhost",
                       "-e", "FRONTEND_ROOT=/srv/frontend/current", "-p", "127.0.0.1::443", "-p", "127.0.0.1::80",
                       "--mount", "type=bind,src=" + str(root / "deployments/caddy/Caddyfile") + ",dst=/etc/caddy/Caddyfile,readonly",
                       "--mount", "type=bind,src=" + str(root / "deployments/frontend/frontend.caddy") + ",dst=/etc/caddy/frontend/site.caddy,readonly",
                       "--mount", "type=bind,src=" + str(fixture) + ",dst=/srv/frontend,readonly",
                       "caddy:2.11.4-alpine")
                ready(edge, "certificate obtained successfully")
                ports = json.loads(docker("inspect", "--format", "{{json .NetworkSettings.Ports}}", edge))
                tls_port = int(ports["443/tcp"][0]["HostPort"])
                http_port = int(ports["80/tcp"][0]["HostPort"])
                certificate = fixture / "root.crt"
                docker("cp", edge + ":/data/caddy/pki/authorities/local/root.crt", str(certificate))
                context = ssl.create_default_context(cafile=str(certificate))
                original_resolver = socket.getaddrinfo

                def local_resolver(host, *args, **kwargs):
                    return original_resolver("127.0.0.1" if host in {"www.localhost", "api.localhost", "localhost"} else host,
                                             *args, **kwargs)

                def request(host, path, method="GET", tls=True):
                    connection = (http.client.HTTPSConnection(host, tls_port, context=context, timeout=15) if tls
                                  else http.client.HTTPConnection(host, http_port, timeout=15))
                    try:
                        connection.request(method, path, headers={"Host": host})
                        response = connection.getresponse()
                        return response.status, dict(response.getheaders()), response.read()
                    finally:
                        connection.close()

                # Act / Assert: only DNS is localized; TLS verifies each real requested hostname.
                with patch.object(socket, "getaddrinfo", side_effect=local_resolver):
                    for path in ("/", "/login", "/lists/01900000-0000-7000-8000-000000000001", "/shared-wishlists/fixture"):
                        status, headers, body = request("www.localhost", path)
                        self.assertEqual(200, status)
                        self.assertIn(b"fixture-app", body)
                        self.assertEqual("no-store", headers["Cache-Control"])
                        self.assertIn("frame-ancestors 'none'", headers["Content-Security-Policy"])
                        self.assertEqual("no-referrer", headers["Referrer-Policy"])
                        self.assertEqual("DENY", headers["X-Frame-Options"])
                        self.assertEqual("nosniff", headers["X-Content-Type-Options"])
                    status, headers, body = request("www.localhost", "/assets/main-abcdefgh.js")
                    self.assertEqual(200, status)
                    self.assertEqual("public, max-age=31536000, immutable", headers["Cache-Control"])
                    self.assertEqual(b"// synthetic fixture", body)
                    for path in ("/assets/missing.js", "/src/main.js", "/.env", "/api/v1/wishlists", "/security/csrf-token", "/main.js.map"):
                        status, headers, body = request("www.localhost", path)
                        self.assertEqual(404, status)
                        self.assertNotIn(b"fixture-app", body)
                    self.assertEqual(405, request("www.localhost", "/login", method="POST")[0])
                    status, headers, _ = request("localhost", "/login?returnPath=%2Flists")
                    self.assertEqual(308, status)
                    self.assertEqual("https://www.monkado.fr/login?returnPath=%2Flists", headers["Location"])
                    status, headers, _ = request("www.localhost", "/login", tls=False)
                    self.assertEqual(308, status)
                    self.assertEqual("https://www.localhost/login", headers["Location"])
                    self.assertEqual(b"api-fixture", request("api.localhost", "/readiness")[2])
                    docker("stop", api)
                    for page in ("legal-notice", "privacy-policy", "terms-of-use"):
                        status, headers, body = request("www.localhost", "/" + page)
                        self.assertEqual(200, status)
                        self.assertEqual(("<h1>" + page + "</h1>").encode(), body)
                        self.assertEqual("no-store", headers["Cache-Control"])
                self.assertEqual({}, json.loads(docker("inspect", "--format", "{{json .HostConfig.PortBindings}}", api)))
            finally:
                for container in (edge, api):
                    subprocess.run(["docker", "rm", "-f", "-v", container], capture_output=True, timeout=30)
                docker("network", "rm", name)


if __name__ == "__main__":
    unittest.main()
