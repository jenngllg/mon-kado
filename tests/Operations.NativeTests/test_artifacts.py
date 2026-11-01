"""Artifact transport is exercised with in-memory responses; no provider request is made."""

import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest

from native_artifacts import download, identity, inspect, location, materialize
from test_release import release


class Response:
    def __init__(self, body=b"", status=200, headers=None):
        self.status = status
        self.headers = headers or {}
        self.body = body

    def getheader(self, name):
        return self.headers.get(name)

    def read(self, maximum):
        body, self.body = self.body[:maximum], self.body[maximum:]
        return body


class Connection:
    def __init__(self, responses):
        self.responses = list(responses)
        self.calls = []
        self.closed = 0

    def __call__(self, host, **options):
        self.calls.append((host, options))
        return self

    def request(self, method, target, headers):
        self.calls[-1] += (method, target, headers)

    def getresponse(self):
        return self.responses.pop(0)

    def close(self):
        self.closed += 1


class TransportTests(unittest.TestCase):
    def test_provider_redirect_preserves_https_allowlist_and_streaming_bound(self):
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary) / "artifact"
            connection = Connection([Response(status=302, headers={"Location": "https://release-assets.githubusercontent.com/release?signature=private"}),
                                     Response(b"native fixture", headers={"Content-Length": "14"})])
            download("https://github.com/jenngllg/mon-kado/releases/download/approved/api", destination, 14,
                     connection=connection, clock=lambda: 1)
            self.assertEqual(b"native fixture", destination.read_bytes())
            self.assertEqual(["github.com", "release-assets.githubusercontent.com"], [item[0] for item in connection.calls])
            self.assertEqual(2, connection.closed)
            self.assertTrue(all(item[2] == "GET" for item in connection.calls))
            self.assertTrue(all(item[1]["context"].check_hostname for item in connection.calls))

    def test_redirects_credentials_insecure_schemes_and_unbounded_responses_are_rejected(self):
        for value in ("http://github.com/asset", "https://evil.test/asset", "https://github.com:444/asset",
                      "https://secret@github.com/asset", "https://github.com/asset#secret", "https://github.com/asset\n"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                location(value)
        for response in (Response(status=500), Response(b"123456"), Response(headers={"Content-Length": "6"}),
                         Response(headers={"Content-Length": "invalid"}), Response(status=302, headers={"Location": "https://evil.test/asset"})):
            with tempfile.TemporaryDirectory() as temporary, self.assertRaises(ValueError):
                download("https://github.com/asset", Path(temporary) / "artifact", 5,
                         connection=Connection([response]), clock=lambda: 1)
        with tempfile.TemporaryDirectory() as temporary, self.assertRaises(ValueError):
            download("https://github.com/asset", Path(temporary) / "artifact", 5,
                     connection=Connection([Response(status=302, headers={"Location": "https://github.com/again"})] * 4), clock=lambda: 1)

    def test_download_budget_is_owned_by_one_entire_redirect_chain(self):
        times = iter([0, 121])
        connection = Connection([])
        with tempfile.TemporaryDirectory() as temporary, self.assertRaises(ValueError):
            download("https://github.com/asset", Path(temporary) / "artifact", 5, connection=connection, clock=lambda: next(times))
        self.assertEqual([], connection.calls)


@unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "native installation ownership requires an isolated Linux root fixture")
class MaterializationTests(unittest.TestCase):
    def test_installation_verifies_every_byte_before_metadata_execution_and_reuses_immutable_set(self):
        value = release()
        body = b"native fixture"
        for artifact in value["binaries"].values():
            artifact.update(size=len(body), sha256=hashlib.sha256(body).hexdigest())
        downloads, commands = [], []
        def transport(url, path, maximum):
            downloads.append((url, maximum))
            path.write_bytes(body)
            path.chmod(0o600)
        def runner(arguments, maximum):
            commands.append(arguments)
            result = value["migrationCatalog"] if arguments[1] == "--catalog" else {
                "schemaVersion": 1, "revision": value["revision"], "os": "linux", "architecture": "amd64"}
            return json.dumps(result).encode()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "releases"
            directory = materialize(root, value, transport)
            self.assertEqual(identity(value), directory.name)
            self.assertEqual(3, len(downloads))
            inspect(directory, value, runner)
            self.assertEqual(4, len(commands))
            self.assertEqual(directory, materialize(root, value, lambda *_: self.fail("immutable set was downloaded again")))
            (directory / "api").write_bytes(b"modified bytes")
            with self.assertRaises(ValueError):
                materialize(root, value, transport)

    def test_partial_or_incorrect_publication_never_replaces_existing_release(self):
        value = release()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "releases"
            def transport(url, path, maximum):
                path.write_bytes(b"incorrect")
                path.chmod(0o600)
            with self.assertRaises(ValueError):
                materialize(root, value, transport)
            self.assertEqual([], list(root.iterdir()))


if __name__ == "__main__":
    unittest.main()
