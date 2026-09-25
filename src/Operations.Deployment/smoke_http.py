"""Bounded HTTPS transport with in-memory credentials and a fixed local destination."""

import http.client
import json
import re
import socket
import ssl
from http.cookies import SimpleCookie
from time import monotonic

from deploy_policy import DeploymentError, ensure

API_HOST = "api.monkado.fr"
FRONTEND_HOST = "www.monkado.fr"
ORIGIN = "https://www.monkado.fr"
MAXIMUM_BODY = 2 * 1024 * 1024
COOKIE_POLICIES = {"__Host-MonKado.Refresh": "strict", "__Host-MonKado.Antiforgery": "lax"}


class LocalHttps(http.client.HTTPSConnection):
    """Resolve only the socket to loopback, preserving hostname validation and TLS SNI."""

    def __init__(self, host, timeout, context):
        super().__init__(host, timeout=timeout, context=context)
        self._create_connection = self.local_connection

    @staticmethod
    def local_connection(address, timeout, source_address=None):
        """Ignore public DNS when probing the installed local reverse proxy."""
        return socket.create_connection(("127.0.0.1", 443), timeout, source_address)


class Client:
    """Never follow redirects or emit request bodies, cookies, tokens or provider errors."""

    def __init__(self, connect=LocalHttps, clock=monotonic, context=None):
        self.connect = connect
        self.clock = clock
        self.context = context or ssl.create_default_context()
        self.context.minimum_version = ssl.TLSVersion.TLSv1_2
        ensure(self.context.verify_mode == ssl.CERT_REQUIRED and self.context.check_hostname, "TLS_VALIDATION_REQUIRED")
        self.cookies = {}
        self.token = None
        self.csrf = None
        self.deadline = self.clock() + 120

    def request(self, method, path, payload=None, headers=None, host=API_HOST):
        """Return bounded response data only to the trusted smoke-test implementation."""
        ensure(host in (API_HOST, FRONTEND_HOST), "SMOKE_TARGET_REFUSED")
        ensure(isinstance(path, str) and re.fullmatch(r"/[A-Za-z0-9_./?=&%-]*", path) is not None
               and not path.startswith("//"), "SMOKE_PATH_REFUSED")
        remaining = self.deadline - self.clock()
        ensure(remaining > 0, "SMOKE_TIMEOUT")
        outgoing = {"User-Agent": "MonKado-deployment-smoke", "Origin": ORIGIN}
        if host == API_HOST:
            if self.cookies:
                outgoing["Cookie"] = "; ".join(name + "=" + value for name, value in self.cookies.items())
            if self.token:
                outgoing["Authorization"] = "Bearer " + self.token
            if self.csrf:
                outgoing["X-CSRF-TOKEN"] = self.csrf
        outgoing.update(headers or {})
        body = None if payload is None else json.dumps(payload).encode()
        if body is not None:
            outgoing["Content-Type"] = "application/json"
        connection = self.connect(host, min(10, remaining), self.context)
        try:
            connection.request(method, path, body=body, headers=outgoing)
            response = connection.getresponse()
            raw = response.read(MAXIMUM_BODY + 1)
            ensure(len(raw) <= MAXIMUM_BODY, "SMOKE_RESPONSE_TOO_LARGE")
            return response.status, self.response_headers(response.getheaders(), host), raw
        except (OSError, http.client.HTTPException):
            raise DeploymentError("SMOKE_CONNECTION_FAILED") from None
        finally:
            connection.close()

    def response_headers(self, pairs, host):
        """Preserve matching headers, rejecting ambiguity and retaining only API cookies."""
        result = {}
        for name, value in pairs:
            key = name.lower()
            if key == "set-cookie" and host == API_HOST:
                self.accept_cookies(value)
            elif key in result:
                ensure(result[key] == value, "CONFLICTING_RESPONSE_HEADERS")
            else:
                result[key] = value
        return result

    def accept_cookies(self, header):
        """Only retain the API's secure host-only session cookies in memory."""
        parsed = SimpleCookie()
        parsed.load(header)
        ensure(bool(parsed), "INVALID_SMOKE_COOKIE")
        for name, cookie in parsed.items():
            ensure(name in COOKIE_POLICIES and cookie["secure"] and cookie["httponly"]
                   and cookie["path"] == "/" and not cookie["domain"]
                   and cookie["samesite"].lower() == COOKIE_POLICIES[name], "INVALID_SMOKE_COOKIE")
            self.cookies[name] = cookie.value

    def json(self, method, path, expected=200, payload=None, headers=None):
        """Validate HTTP success and JSON without exposing a failed response body."""
        status, response_headers, raw = self.request(method, path, payload, headers)
        ensure(status == expected, "SMOKE_HTTP_STATUS")
        ensure("no-store" in response_headers.get("cache-control", ""), "SMOKE_CACHE_POLICY")
        if expected == 204:
            ensure(not raw, "SMOKE_CONTRACT_FAILED")
            return None, response_headers
        ensure(response_headers.get("content-type", "").startswith("application/json"), "SMOKE_CONTRACT_FAILED")
        try:
            return json.loads(raw), response_headers
        except ValueError:
            raise DeploymentError("SMOKE_CONTRACT_FAILED") from None

    def clear(self):
        """Drop credentials even when a smoke test or session closure fails."""
        self.cookies.clear()
        self.token = None
        self.csrf = None
