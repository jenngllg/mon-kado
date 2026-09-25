"""Check response contracts and redirection using controlled HTTP responses."""

import http.client
import json
import unittest
from unittest.mock import Mock

from deploy_policy import DeploymentError
from smoke_http import ORIGIN
from smoke_technical import SECURITY, check, frontend, redirect


class TechnicalTests(unittest.TestCase):
    def test_complete_readonly_contract(self):
        headers = SECURITY | {"access-control-allow-origin": ORIGIN, "access-control-allow-credentials": "true", "cache-control": "no-store"}
        client = Mock(spec=["request"])
        client.request.side_effect = [(200, headers, b"Healthy"), (200, headers, b"Healthy"),
            (200, headers, json.dumps({"openapi": "3.1.0", "paths": {"/api/v1/auth/sessions": {}, "/api/v1/wishlists": {}}}).encode()),
            (200, headers, json.dumps({"token": "a" * 32}).encode()), (204, headers, b""), (204, {}, b"")]
        check(client)
        self.assertEqual(6, client.request.call_count)

    def test_rejects_200_with_missing_security(self):
        client = Mock()
        client.request.return_value = (200, {}, b"Healthy")
        with self.assertRaisesRegex(DeploymentError, "SECURITY_HEADERS_FAILED"):
            check(client)

    def test_frontend_marker_is_preserved(self):
        value = {"revision": "a" * 40, "apiOrigin": "https://api.monkado.fr", "googleEnabled": False}
        client = Mock()
        client.request.side_effect = [(200, {}, b"html"), (200, {}, json.dumps(value).encode())]
        self.assertEqual(value, frontend(client))
        self.assertTrue(all(call.kwargs["host"] == "www.monkado.fr" for call in client.request.call_args_list))

    def test_redirect_validates_status_and_destination_and_closes(self):
        response = Mock(status=308)
        response.getheader.return_value = "https://api.monkado.fr/liveness"
        connection = Mock()
        connection.getresponse.return_value = response
        factory = Mock(return_value=connection)
        redirect(factory)
        factory.assert_called_once_with("127.0.0.1", 80, timeout=10)
        response.status = 200
        with self.assertRaisesRegex(DeploymentError, "HTTPS_REDIRECT_FAILED"):
            redirect(factory)
        connection.request.side_effect = http.client.HTTPException("private")
        with self.assertRaisesRegex(DeploymentError, "^HTTPS_REDIRECT_FAILED$"):
            redirect(factory)
        self.assertEqual(3, connection.close.call_count)


if __name__ == "__main__":
    unittest.main()
