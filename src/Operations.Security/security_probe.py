"""Four read-only HTTP checks; never print headers, cookies or response bodies."""

import json
import urllib.error
import urllib.request

API = "https://api.monkado.fr"
FRONTEND = "https://www.monkado.fr"


class NoRedirect(urllib.request.HTTPRedirectHandler):
    """Inspect the HTTP redirect itself rather than silently following it."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def fetch(url, method, headers):
    """Verify HTTPS certificates and return only status and response headers internally."""
    request = urllib.request.Request(url, method=method, headers=headers)
    opener = urllib.request.build_opener(NoRedirect())
    try:
        response = opener.open(request, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, {key.lower(): value for key, value in response.headers.items()}


def verify(send=fetch):
    """Check the fixed production origins without authentication, mutation or load testing."""
    http_status, http_headers = send(API.replace("https:", "http:") + "/readiness", "HEAD", {})
    status, headers = send(API + "/readiness", "HEAD", {})
    preflight = {"Origin": FRONTEND, "Access-Control-Request-Method": "GET",
                 "Access-Control-Request-Headers": "authorization"}
    allowed_status, allowed = send(API + "/api/v1/wishlists", "OPTIONS", preflight)
    denied_status, denied = send(API + "/api/v1/wishlists", "OPTIONS", dict(preflight, Origin="https://untrusted.invalid"))
    return {
        "httpsHealthy": status == 200,
        "canonicalHttpsRedirect": http_status == 308 and http_headers.get("location") == API + "/readiness",
        "hsts": headers.get("strict-transport-security") == "max-age=31536000",
        "apiCsp": headers.get("content-security-policy") == "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
        "nosniff": headers.get("x-content-type-options") == "nosniff",
        "noFraming": headers.get("x-frame-options") == "DENY",
        "noReferrer": headers.get("referrer-policy") == "no-referrer",
        "allowedOrigin": allowed_status == 204 and allowed.get("access-control-allow-origin") == FRONTEND,
        "credentialsAllowed": allowed.get("access-control-allow-credentials") == "true",
        "foreignOriginNotGranted": denied_status < 500 and "access-control-allow-origin" not in denied,
    }


def main(check=verify):
    """Print bounded boolean evidence or one generic transport failure."""
    try:
        result = check()
    except Exception:
        print('{"error":"SECURITY_PROBE_UNAVAILABLE"}')
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0 if all(result.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
