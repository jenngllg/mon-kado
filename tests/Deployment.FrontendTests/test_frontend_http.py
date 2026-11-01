"""Real native TLS/static hosting checks inside a private Linux network namespace."""

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
from native_fixture import NativeCaddy, stop


@unittest.skipUnless(os.environ.get("MONKADO_FRONTEND_NATIVE_TESTS") == "1", "Explicit isolated native opt-in required")
class FrontendHttpTests(unittest.TestCase):
    def test_static_site_and_api_share_caddy_without_exposing_internal_ports(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
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
  if self.path == '/api/v1/shared-wishlists/01900000-0000-7000-8000-000000000001/preview':
   self.send_response(200)
   self.send_header('Content-Type','text/html; charset=utf-8')
   self.end_headers()
   self.wfile.write(b'<meta property="og:title" content="Public preview">')
   return
  if self.path == '/api/v1/shared-wishlists/01900000-0000-7000-8000-000000000001/preview/image':
   self.send_response(200)
   self.send_header('Content-Type','image/jpeg')
   self.end_headers()
   self.wfile.write(b'preview-image-fixture')
   return
  self.send_response(200)
  self.end_headers()
  self.wfile.write(b'api-fixture')
 def log_message(self,*args): pass
server=http.server.HTTPServer(('127.0.0.1',7000),Handler)
print('fixture-ready',flush=True)
server.serve_forever()
"""
            configuration = (root / 'deployments/caddy/Caddyfile').read_text().replace(
                'import /opt/monkado/deployments/caddy/frontend/*.caddy',
                'import ' + str(root / 'deployments/frontend/frontend.caddy'))
            environment = {'API_HOST':'api.localhost','FRONTEND_HOST':'www.localhost',
                           'FRONTEND_APEX_HOST':'localhost','FRONTEND_API_ORIGIN':'http://127.0.0.1:7000',
                           'FRONTEND_ROOT':str(current)}
            with NativeCaddy(root, configuration, environment, server) as native:
                tls_port, http_port = 443, 80
                context = ssl.create_default_context(cafile=str(native.certificate))
                original_resolver = socket.getaddrinfo

                def local_resolver(host, *args, **kwargs):
                    return original_resolver("127.0.0.1" if host in {"www.localhost", "api.localhost", "localhost"} else host,
                                             *args, **kwargs)

                def request(host, path, method="GET", tls=True, user_agent="browser-fixture"):
                    connection = (http.client.HTTPSConnection(host, tls_port, context=context, timeout=15) if tls
                                  else http.client.HTTPConnection(host, http_port, timeout=15))
                    try:
                        connection.request(method, path, headers={"Host": host, "User-Agent": user_agent})
                        response = connection.getresponse()
                        return response.status, dict(response.getheaders()), response.read()
                    finally:
                        connection.close()

                # Act / Assert: only DNS is localized; TLS verifies each real requested hostname.
                with patch.object(socket, "getaddrinfo", side_effect=local_resolver):
                    shared_path = "/shared-wishlists/01900000-0000-7000-8000-000000000001"
                    self.assertIn(b"fixture-app", request("www.localhost", shared_path)[2])
                    for agent in ("facebookexternalhit/1.1", "Discordbot/2.0", "WhatsApp/2.0"):
                        status, headers, body = request("www.localhost", shared_path, user_agent=agent)
                        self.assertEqual(200, status)
                        self.assertIn(b'property="og:title"', body)
                        self.assertNotIn(b"fixture-app", body)
                        self.assertEqual("no-store", headers["Cache-Control"])
                    status, headers, body = request("www.localhost", "/share-previews/01900000-0000-7000-8000-000000000001/image")
                    self.assertEqual(200, status)
                    self.assertEqual("image/jpeg", headers["Content-Type"])
                    self.assertEqual(b"preview-image-fixture", body)
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
                    stop(native.upstream)
                    for page in ("legal-notice", "privacy-policy", "terms-of-use"):
                        status, headers, body = request("www.localhost", "/" + page)
                        self.assertEqual(200, status)
                        self.assertEqual(("<h1>" + page + "</h1>").encode(), body)
                        self.assertEqual("no-store", headers["Cache-Control"])


if __name__ == "__main__":
    unittest.main()
