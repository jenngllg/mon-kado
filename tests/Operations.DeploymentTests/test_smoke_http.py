"""Exercise TLS targeting, bounded output and secret handling without network access."""

import http.client
import json
import ssl
import unittest
from unittest.mock import Mock, patch

from deploy_policy import DeploymentError
from smoke_http import API_HOST, FRONTEND_HOST, MAXIMUM_BODY, Client, LocalHttps


class HttpTests(unittest.TestCase):
    def setUp(self):
        self.response = Mock(spec=["read", "getheaders", "status"])
        self.response.status = 200
        self.response.read.return_value = b'{"safe":true}'
        self.response.getheaders.return_value = [("Content-Type", "application/json"), ("Cache-Control", "no-store")]
        self.connection = Mock(spec=["request", "getresponse", "close"])
        self.connection.getresponse.return_value = self.response
        self.connect = Mock(return_value=self.connection)
        self.client = Client(connect=self.connect, clock=lambda: 0)

    def test_verified_hostname_uses_only_local_socket(self):
        with patch("smoke_http.socket.create_connection", return_value="socket") as connect:
            connection = LocalHttps(API_HOST, 5, ssl.create_default_context())
            self.assertEqual("socket", connection._create_connection((API_HOST, 443), 5))
            connect.assert_called_once_with(("127.0.0.1", 443), 5, None)

    def test_refuses_insecure_tls_context(self):
        context = ssl.create_default_context()
        context.check_hostname = False
        with self.assertRaisesRegex(DeploymentError, "TLS_VALIDATION_REQUIRED"):
            Client(context=context)

    def test_credentials_only_sent_to_api_and_never_in_result(self):
        self.client.token = "synthetic-token"
        self.client.csrf = "synthetic-csrf"
        self.client.cookies["__Host-MonKado.Refresh"] = "synthetic-cookie"
        result = self.client.json("POST", "/api/v1/auth/sessions", payload={"test": True}, headers={"If-Match": '"00000001"'})
        self.assertEqual({"safe": True}, result[0])
        sent = self.connection.request.call_args.kwargs
        self.assertEqual("Bearer synthetic-token", sent["headers"]["Authorization"])
        self.assertEqual("synthetic-csrf", sent["headers"]["X-CSRF-TOKEN"])
        self.assertEqual("__Host-MonKado.Refresh=synthetic-cookie", sent["headers"]["Cookie"])
        self.assertEqual({"test": True}, json.loads(sent["body"]))
        self.client.request("GET", "/", host=FRONTEND_HOST)
        sent = self.connection.request.call_args.kwargs
        self.assertNotIn("Cookie", sent["headers"])
        self.assertNotIn("Authorization", sent["headers"])
        self.client.clear()
        self.assertEqual({}, self.client.cookies)
        self.assertIsNone(self.client.csrf)
        self.assertIsNone(self.client.token)

    def test_accepts_only_secure_host_only_cookies(self):
        valid = "__Host-MonKado.Refresh=value; Secure; HttpOnly; Path=/; SameSite=Strict"
        self.response.getheaders.return_value.append(("Set-Cookie", valid))
        self.client.request("GET", "/")
        self.assertEqual({"__Host-MonKado.Refresh": "value"}, self.client.cookies)
        for cookie in ("not a cookie", valid.replace("Secure;", ""), valid + "; Domain=monkado.fr",
                       valid.replace("HttpOnly;", ""), valid.replace("Path=/;", "Path=/other;"), "other=x; Secure; HttpOnly; Path=/",
                       valid.replace("SameSite=Strict", "SameSite=None"), valid.replace("SameSite=Strict", "")):
            with self.subTest(cookie=cookie), self.assertRaisesRegex(DeploymentError, "INVALID_SMOKE_COOKIE"):
                self.client.accept_cookies(cookie)

    def test_target_path_deadline_and_response_bounds(self):
        for path, host in (("/", "untrusted.invalid"), ("https://external/", API_HOST), ("//external", API_HOST), (None, API_HOST)):
            with self.subTest(path=path), self.assertRaises(DeploymentError):
                self.client.request("GET", path, host=host)
        self.client.deadline = 0
        with self.assertRaisesRegex(DeploymentError, "SMOKE_TIMEOUT"):
            self.client.request("GET", "/")
        self.client.deadline = 120
        self.response.read.return_value = b"x" * (MAXIMUM_BODY + 1)
        with self.assertRaisesRegex(DeploymentError, "SMOKE_RESPONSE_TOO_LARGE"):
            self.client.request("GET", "/")
        self.connection.close.assert_called_once()

    def test_conflicting_headers_and_connection_errors_are_safe(self):
        self.response.getheaders.return_value += [("Content-Type", "application/json")]
        self.client.request("GET", "/")
        self.response.getheaders.return_value += [("Content-Type", "text/plain")]
        with self.assertRaisesRegex(DeploymentError, "CONFLICTING_RESPONSE_HEADERS"):
            self.client.request("GET", "/")
        for failure in (OSError("sensitive payload"), http.client.HTTPException("sensitive payload")):
            self.connection.request.side_effect = failure
            with self.subTest(error=type(failure)), self.assertRaisesRegex(DeploymentError, "^SMOKE_CONNECTION_FAILED$"):
                self.client.request("GET", "/")

    def test_json_status_content_type_cache_and_empty_response_contract(self):
        for status, headers, body, expected in (
            (429, {}, b"private provider output", 200),
            (200, {"content-type": "application/json"}, b"{}", 200),
            (200, {"cache-control": "no-store"}, b"{}", 200),
            (200, {"cache-control": "no-store", "content-type": "application/json"}, b"invalid", 200),
            (204, {"cache-control": "no-store"}, b"unexpected", 204),
        ):
            self.response.status = status
            self.response.getheaders.return_value = list(headers.items())
            self.response.read.return_value = body
            with self.subTest(status=status, body=body), self.assertRaises(DeploymentError):
                self.client.json("GET", "/", expected)
        self.response.read.return_value = b""
        self.assertIsNone(self.client.json("DELETE", "/", expected=204)[0])


if __name__ == "__main__":
    unittest.main()
