"""Regression tests for the trust boundary between a release and root deployment."""

import contextlib
import importlib.util
import io
import json
import runpy
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("release_manifest", Path(__file__).with_name("release_manifest.py"))
manifest = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manifest)


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.value = {
            "schemaVersion": 1,
            "revision": "a" * 40,
            "configurationHash": "b" * 64,
            "apiImage": "ghcr.io/jenngllg/mon-kado-api@sha256:" + "c" * 64,
            "workerImage": "ghcr.io/jenngllg/mon-kado-worker@sha256:" + "d" * 64,
        }

    def test_valid_contract(self):
        self.assertEqual(self.value, manifest.validate(self.value, "b" * 64))

    def test_v2_binds_unique_approval_and_complete_migration_catalog(self):
        catalog = {"schemaVersion": 1, "modelHash": "a" * 64,
                   "migrations": [{"id": "20260101000000_Initial", "sha256": "c" * 64}]}
        value = self.value | {"schemaVersion": 2, "publicationId": "123456-1", "migrationCatalog": catalog,
                              "migrationHash": manifest.fingerprint(catalog), "rollbackAllowed": True}
        self.assertEqual(value, manifest.validate(value, "b" * 64))
        for change in ({"publicationId": None}, {"publicationId": "unsafe"}, {"publicationId": "1２-1"}, {"rollbackAllowed": 1},
                       {"migrationHash": "b" * 64}, {"migrationCatalog": {}}, {"extra": True}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                manifest.validate(value | change, "b" * 64)

    def test_invalid_fields(self):
        for value in (None, [], {}, {**self.value, "command": "whoami"}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                manifest.validate(value, "b" * 64)

    def test_invalid_values(self):
        invalid = {
            "schemaVersion": [True, "1", 2, None],
            "revision": [None, "develop", "a" * 39, "a" * 40 + "\n"],
            "configurationHash": [None, "e" * 64],
            "apiImage": [None, "ghcr.io/jenngllg/mon-kado-api:latest", "$(whoami)",
                         "ghcr.io/other/mon-kado-api@sha256:" + "c" * 64,
                         "ghcr.io/jenngllg/mon-kado-api@sha256:" + "c" * 64 + "\nEVIL=1"],
            "workerImage": [None, "ghcr.io/jenngllg/mon-kado-worker:latest", self.value["apiImage"]],
        }
        for key, values in invalid.items():
            for value in values:
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    manifest.validate({**self.value, key: value}, "b" * 64)

    def test_production_release(self):
        release = {"tag_name": "backend-production", "draft": False, "prerelease": False,
                   "body": json.dumps(self.value)}
        self.assertEqual(self.value, manifest.from_release(release, "b" * 64))
        for key, value in (("tag_name", "latest"), ("draft", True), ("prerelease", True),
                           ("body", None), ("body", "x" * 4097), ("body", "invalid json")):
            with self.subTest(key=key), self.assertRaises(ValueError):
                manifest.from_release({**release, key: value}, "b" * 64)
        with self.assertRaises(ValueError):
            manifest.from_release([], "b" * 64)

    def test_hash_covers_every_installed_file_and_normalizes_line_endings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in manifest.FILES:
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(b"original\n")
            original = manifest.configuration_hash(root)
            for name in manifest.FILES:
                target = root / name
                target.write_bytes(b"changed\n")
                self.assertNotEqual(original, manifest.configuration_hash(root))
                target.write_bytes(b"original\r\n")
                self.assertEqual(original, manifest.configuration_hash(root))

    def test_cli_hash(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            manifest.main(["hash"])
        self.assertRegex(output.getvalue(), r"^[0-9a-f]{64}\n$")

    def test_cli_release(self):
        self.value["configurationHash"] = manifest.configuration_hash(Path(__file__).resolve().parents[2])
        release = {"tag_name": "backend-production", "draft": False, "prerelease": False,
                   "body": json.dumps(self.value)}
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "release.json"
            target.write_text(json.dumps(release), encoding="utf-8")
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                manifest.main(["release", str(target)])
            self.assertEqual(f'API_IMAGE={self.value["apiImage"]}\n'
                             f'WORKER_IMAGE={self.value["workerImage"]}\n'
                             f'RELEASE_REVISION={self.value["revision"]}\n',
                             output.getvalue())
            target.write_bytes(b"x" * 65537)
            with self.assertRaises(ValueError):
                manifest.main(["release", str(target)])

    def test_cli_arguments(self):
        for arguments in ([], ["unknown"], ["release"], ["release", "one", "two"]):
            with self.subTest(arguments=arguments), self.assertRaises(ValueError):
                manifest.main(arguments)

    def test_entry_point(self):
        script = str(Path(__file__).with_name("release_manifest.py"))
        with patch.object(sys, "argv", [script, "hash"]), contextlib.redirect_stdout(io.StringIO()):
            runpy.run_path(script, run_name="__main__")
        error = io.StringIO()
        with patch.object(sys, "argv", [script]), contextlib.redirect_stderr(error):
            with self.assertRaises(SystemExit) as result:
                runpy.run_path(script, run_name="__main__")
        self.assertEqual(1, result.exception.code)
        self.assertEqual("Invalid release or local configuration; deployment refused.\n", error.getvalue())


if __name__ == "__main__":
    unittest.main()
