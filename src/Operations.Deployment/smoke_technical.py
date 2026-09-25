"""Read-only deployment probes validate API contracts and the shared frontend proxy."""

import http.client
import json
import re

from deploy_policy import DeploymentError, ensure
from smoke_http import API_HOST, FRONTEND_HOST, ORIGIN

LIVENESS_PATH = "/liveness"
WISHLISTS_PATH = "/api/v1/wishlists"
SECURITY = {
    "strict-transport-security": "max-age=31536000",
    "content-security-policy": "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
    "x-content-type-options": "nosniff", "x-frame-options": "DENY", "referrer-policy": "no-referrer",
    "permissions-policy": "camera=(), microphone=(), geolocation=()",
}


def redirect(connect=http.client.HTTPConnection):
    """Probe loopback port 80 with the configured Host, without following its redirect."""
    connection = connect("127.0.0.1", 80, timeout=10)
    try:
        connection.request("GET", LIVENESS_PATH, headers={"Host": API_HOST})
        response = connection.getresponse()
        ensure(response.status in (301, 308) and response.getheader("Location") == "https://" + API_HOST + LIVENESS_PATH,
               "HTTPS_REDIRECT_FAILED")
    except (OSError, http.client.HTTPException):
        raise DeploymentError("HTTPS_REDIRECT_FAILED") from None
    finally:
        connection.close()


def check(client):
    """Validate success contracts, API headers and exact credentialed CORS without mutations."""
    for path in (LIVENESS_PATH, "/readiness", "/openapi/v1.json", "/security/csrf-token"):
        status, headers, body = client.request("GET", path)
        ensure(status == 200, "TECHNICAL_SMOKE_FAILED")
        ensure(all(headers.get(key) == value for key, value in SECURITY.items()), "SECURITY_HEADERS_FAILED")
        ensure(headers.get("access-control-allow-origin") == ORIGIN and
               headers.get("access-control-allow-credentials") == "true", "CORS_SMOKE_FAILED")
        if path in (LIVENESS_PATH, "/readiness"):
            ensure(body == b"Healthy", "HEALTH_CONTRACT_FAILED")
        elif path == "/openapi/v1.json":
            value = json.loads(body)
            ensure(isinstance(value, dict) and value.get("openapi", "").startswith("3.") and
                   "/api/v1/auth/sessions" in value.get("paths", {}) and WISHLISTS_PATH in value["paths"],
                   "OPENAPI_CONTRACT_FAILED")
        else:
            value = json.loads(body)
            ensure(isinstance(value, dict) and isinstance(value.get("token"), str) and len(value["token"]) >= 32
                   and "no-store" in headers.get("cache-control", ""), "CSRF_CONTRACT_FAILED")
    status, headers, _ = client.request("OPTIONS", WISHLISTS_PATH, headers={
        "Access-Control-Request-Method": "POST", "Access-Control-Request-Headers": "authorization,content-type"})
    ensure(status == 204 and headers.get("access-control-allow-origin") == ORIGIN and
           headers.get("access-control-allow-credentials") == "true", "CORS_SMOKE_FAILED")
    _, headers, _ = client.request("OPTIONS", WISHLISTS_PATH, headers={
        "Origin": "https://untrusted.invalid", "Access-Control-Request-Method": "POST"})
    ensure("access-control-allow-origin" not in headers, "CORS_SMOKE_FAILED")


def frontend(client):
    """Return only the public frontend revision marker for comparison across the rollout."""
    status, _, _ = client.request("GET", "/", host=FRONTEND_HOST)
    ensure(status == 200, "FRONTEND_SMOKE_FAILED")
    status, _, raw = client.request("GET", "/release.json", host=FRONTEND_HOST)
    ensure(status == 200, "FRONTEND_SMOKE_FAILED")
    value = json.loads(raw)
    ensure(isinstance(value, dict) and set(value) == {"revision", "apiOrigin", "googleEnabled"} and
           value["apiOrigin"] == "https://" + API_HOST and type(value["googleEnabled"]) is bool and
           isinstance(value["revision"], str) and re.fullmatch(r"[0-9a-f]{40}", value["revision"]) is not None,
           "FRONTEND_SMOKE_FAILED")
    return value
