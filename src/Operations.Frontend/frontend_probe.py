"""Bounded certificate-verified HTTP checks against the local production virtual hosts."""

import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import subprocess
import tempfile
import time

import frontend_contract as contract

FRONTEND = "https://www.monkado.fr"
MAX_BODY = 20 * 1024 * 1024
TOTAL_SECONDS = 120
CONTENT_TYPES = {".html": {"text/html"}, ".js": {"text/javascript", "application/javascript"},
                 ".css": {"text/css"}, ".json": {"application/json"}}


class Entrypoint(HTMLParser):
    """Require the entrypoint's executable/style references to exist in the verified archive."""

    def __init__(self):
        super().__init__()
        self.references = set()
        self.scripts = 0

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "script":
            reference = values.get("src", "")
            suffix = ".js"
            self.scripts += 1
        elif tag == "link" and values.get("rel") in {"stylesheet", "modulepreload"}:
            reference = values.get("href", "")
            suffix = ".css" if values["rel"] == "stylesheet" else ".js"
        else:
            return
        contract.require(isinstance(reference, str) and reference.startswith("/assets/") and reference.endswith(suffix))
        contract.require(contract.allowed_file(reference[1:]))
        self.references.add(reference[1:])


def headers(raw):
    """Reject duplicate headers and redirects rather than accepting contradictory protections."""
    lines = raw.decode("iso-8859-1").strip().splitlines()
    contract.require(bool(lines) and len(lines[0].split()) >= 2 and lines[0].split()[1] == "200")
    result = {}
    for line in lines[1:]:
        key, separator, value = line.partition(":")
        contract.require(bool(separator))
        key = key.strip().lower()
        contract.require(key not in result)
        result[key] = value.strip()
    return result


def validate_headers(values, suffix, immutable=False):
    """Check browser protections and distinguish real static responses from SPA fallbacks."""
    contract.require(values.get("content-type", "").split(";", 1)[0].lower() in CONTENT_TYPES[suffix])
    expected = {"x-content-type-options": "nosniff", "x-frame-options": "DENY", "referrer-policy": "no-referrer",
                "strict-transport-security": "max-age=31536000"}
    contract.require(all(values.get(key) == value for key, value in expected.items()))
    contract.require("server" not in values and "location" not in values and "set-cookie" not in values)
    contract.require(values.get("cache-control") == ("public, max-age=31536000, immutable" if immutable else "no-store"))
    directives = {}
    for directive in values.get("content-security-policy", "").split(";"):
        parts = directive.split()
        if parts:
            contract.require(parts[0] not in directives)
            directives[parts[0]] = parts[1:]
    for name, expected_sources in {"default-src": ["'none'"], "script-src": ["'self'"],
                                   "base-uri": ["'none'"], "object-src": ["'none'"],
                                   "frame-ancestors": ["'none'"], "form-action": ["'self'"],
                                   "connect-src": ["'self'", contract.API_ORIGIN]}.items():
        contract.require(directives.get(name) == expected_sources)
    contract.require(values.get("permissions-policy") ==
                     "camera=(), microphone=(), geolocation=(), payment=(), usb=(), clipboard-write=(self)")


def request(url, directory, remaining, runner):
    """Never follow redirects, disable certificate checks or inherit a proxy from the environment."""
    header_path = directory / "headers"
    body_path = directory / "body"
    timeout = min(15, remaining)
    contract.require(timeout > 0)
    runner(["curl", "--fail", "--silent", "--show-error", "--noproxy", "*", "--proto", "=https",
            "--connect-timeout", str(min(5, timeout)), "--max-time", str(timeout), "--max-filesize", str(MAX_BODY),
            "--resolve", "www.monkado.fr:443:127.0.0.1", "--resolve", "api.monkado.fr:443:127.0.0.1",
            "--dump-header", str(header_path), "--output", str(body_path), url],
           check=True, capture_output=True, timeout=timeout + 1)
    contract.require(header_path.stat().st_size <= 16384 and body_path.stat().st_size <= MAX_BODY)
    return headers(header_path.read_bytes()), body_path.read_bytes()


def probe(revision, directory, google_enabled=False, runner=subprocess.run, clock=time.monotonic):
    """Compare served bytes with the already verified immutable artifact within one deadline."""
    directory = Path(directory)
    files = ["release.json", "index.html"]
    files.extend(sorted(path.relative_to(directory).as_posix() for path in (directory / "assets").glob("*")
                        if path.suffix in {".js", ".css"}))
    files.extend(sorted(name for name in contract.LEGAL_PAGES if (directory / name).is_file()))
    contract.require(any(name.endswith(".js") for name in files))
    entrypoint = Entrypoint()
    entrypoint.feed((directory / "index.html").read_text(encoding="utf-8"))
    contract.require(entrypoint.scripts > 0 and entrypoint.references <= set(files))
    deadline = clock() + TOTAL_SECONDS
    with tempfile.TemporaryDirectory(prefix="frontend-probe-") as temporary:
        scratch = Path(temporary)
        for name in files:
            contract.require(contract.allowed_file(name))
            route = "/" if name == "index.html" else "/" + (Path(name).stem if name in contract.LEGAL_PAGES else name)
            values, body = request(FRONTEND + route, scratch, deadline - clock(), runner)
            immutable = name.startswith("assets/")
            validate_headers(values, Path(name).suffix, immutable)
            contract.require(hashlib.sha256(body).hexdigest() == contract.file_digest(directory / name))
            if name == "release.json":
                contract.require(json.loads(body) == contract.release_marker(revision, google_enabled))
        values, body = request(contract.API_ORIGIN + "/readiness", scratch, deadline - clock(), runner)
        contract.require(values.get("content-type", "").split(";", 1)[0] == "text/plain")
        contract.require(body == b"Healthy")
