"""Synthetic trust-boundary fixtures; no provider or existing installation is used."""

import io
import json
import os
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Frontend"))
import frontend_contract as contract


class ContractTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.dist = self.root / "dist"
        self.dist.mkdir()
        (self.dist / "index.html").write_text('<script type="module" src="/assets/main-abcdefgh.js"></script>')
        (self.dist / "assets").mkdir()
        (self.dist / "assets/main-abcdefgh.js").write_text("// synthetic fixture")
        self.output = self.root / "output"
        self.manifest = contract.package_build(self.dist, self.output, "a" * 40, "b" * 40, "c" * 64)

    def test_roundtrip_and_canonical_pointer(self):
        # Arrange
        release = {"tag_name": "frontend-production", "draft": False, "prerelease": False,
                   "body": json.dumps(self.manifest)}
        target = self.root / "unpacked"
        # Act
        parsed = contract.from_release(release, "c" * 64, "b" * 40)
        contract.extract(self.output / "frontend.tar.gz", target, parsed)
        # Assert
        self.assertEqual(self.manifest, parsed)
        self.assertEqual((self.dist / "index.html").read_bytes(), (target / "index.html").read_bytes())
        self.assertEqual("a" * 40, json.loads((target / "release.json").read_text())["revision"])
        self.assertEqual("https://github.com/jenngllg/mon-kado-front/releases/download/frontend-" + "a" * 40 + "/frontend.tar.gz",
                         contract.archive_url(parsed))
        self.assertEqual(self.manifest, json.loads((self.output / "manifest.json").read_text()))

    def test_packaging_does_not_depend_on_build_timestamps_or_directory(self):
        # Arrange
        for path in self.dist.rglob("*"):
            os.utime(path, (100, 100))
        # Act
        result = contract.package_build(self.dist, self.root / "again", "a" * 40, "b" * 40, "c" * 64)
        # Assert
        self.assertEqual(self.manifest, result)

    def test_manifest_rejects_wrong_types_identity_and_configuration(self):
        # Arrange / Act / Assert
        invalid = {"schemaVersion": [True, "1", 2], "revision": [None, "develop", "a" * 40 + "\n"],
                   "backendRevision": [None, "d" * 40], "configurationHash": [None, "d" * 64],
                   "archiveSha256": [None, "latest"], "apiOrigin": [None, "https://evil.invalid"]}
        for key, values in invalid.items():
            for value in values:
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    contract.validate({**self.manifest, key: value}, "c" * 64, "b" * 40)
        for value in (None, [], {}, {**self.manifest, "script": "do-not-execute"}):
            with self.assertRaises(ValueError):
                contract.validate(value, "c" * 64, "b" * 40)
        with self.assertRaises(ValueError):
            contract.archive_url({"revision": "../escape"})

    def test_unapproved_release_is_rejected(self):
        # Arrange
        release = {"tag_name": "frontend-production", "draft": False, "prerelease": False,
                   "body": json.dumps(self.manifest)}
        invalid = {"tag_name": [None, "latest"], "draft": [None, True], "prerelease": [None, True],
                   "body": [None, "a" * 4097, "not-json"]}
        # Act / Assert
        for key, values in invalid.items():
            for value in values:
                with self.assertRaises(ValueError):
                    contract.from_release({**release, key: value}, "c" * 64, "b" * 40)
        with self.assertRaises(ValueError):
            contract.from_release(None, "c" * 64, "b" * 40)

    def write_archive(self, entries):
        archive = self.root / "hostile.tar.gz"
        with tarfile.open(archive, "w:gz") as package:
            for name, kind, content in entries:
                entry = tarfile.TarInfo(name)
                entry.type = kind
                entry.size = len(content)
                package.addfile(entry, io.BytesIO(content))
        return archive, {**self.manifest, "archiveSha256": contract.file_digest(archive)}

    def test_archive_names_links_duplicates_and_missing_entrypoint_are_rejected(self):
        # Arrange / Act / Assert
        for entries in [
            [(name, tarfile.REGTYPE, b"bad")]
            for name in ("../index.html", "/index.html", "assets/../../key", "assets/.env", "assets/main.js.map",
                         "src/main.js", "assets\\main.js", "index.html\n")
        ] + [
            [("index.html", kind, b"")] for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.DIRTYPE, tarfile.FIFOTYPE)
        ] + [
            [("index.html", tarfile.REGTYPE, b""), ("index.html", tarfile.REGTYPE, b"")],
            [("assets/app.js", tarfile.REGTYPE, b"")],
            [("index.html", tarfile.REGTYPE, b"")],
            [("release.json", tarfile.REGTYPE, b"{}")],
        ]:
            archive, manifest = self.write_archive(entries)
            destination = self.root / "never-created"
            with self.assertRaises(ValueError):
                contract.extract(archive, destination, manifest)
            self.assertFalse(destination.exists())

    def test_checksum_limits_existing_destination_and_wrong_marker_fail_closed(self):
        # Arrange
        archive = self.output / "frontend.tar.gz"
        # Act / Assert
        for key, bound in (("MAX_COMPRESSED", 1), ("MAX_EXTRACTED", 1), ("MAX_FILES", 1)):
            with patch.object(contract, key, bound), self.assertRaises(ValueError):
                contract.extract(archive, self.root / "limited", self.manifest)
        with self.assertRaises(ValueError):
            contract.extract(archive, self.root / "wrong", {**self.manifest, "archiveSha256": "0" * 64})
        with self.assertRaises(ValueError):
            contract.extract(archive, self.dist, self.manifest)
        archive, manifest = self.write_archive([("index.html", tarfile.REGTYPE, b""),
                                                ("release.json", tarfile.REGTYPE, b"{}")])
        with self.assertRaises(ValueError):
            contract.extract(archive, self.root / "wrong-marker", manifest)

    def test_package_rejects_private_files_and_bad_builds(self):
        # Arrange / Act / Assert
        for revision, backend, configuration in (("main", "b" * 40, "c" * 64),
                                                 ("a" * 40, "develop", "c" * 64),
                                                 ("a" * 40, "b" * 40, None)):
            with self.assertRaises(ValueError):
                contract.package_build(self.dist, self.root / "unused", revision, backend, configuration)
        for source in (self.root / "missing", self.dist / "index.html"):
            with self.assertRaises(ValueError):
                contract.package_build(source, self.root / "unused", "a" * 40, "b" * 40, "c" * 64)
        with self.assertRaises(ValueError):
            contract.package_build(self.dist, self.output, "a" * 40, "b" * 40, "c" * 64)
        for private in (".env", "private.key", "release.json", "assets/app.js.map"):
            path = self.dist / private
            path.write_text("synthetic-only")
            with self.assertRaises(ValueError):
                contract.package_build(self.dist, self.root / "unused", "a" * 40, "b" * 40, "c" * 64)
            path.unlink()
        for key, bound in (("MAX_EXTRACTED", 1), ("MAX_FILES", 1), ("MAX_COMPRESSED", 1)):
            with patch.object(contract, key, bound), self.assertRaises(ValueError):
                contract.package_build(self.dist, self.root / key, "a" * 40, "b" * 40, "c" * 64)
        (self.dist / "index.html").unlink()
        with self.assertRaises(ValueError):
            contract.package_build(self.dist, self.root / "unused", "a" * 40, "b" * 40, "c" * 64)
        empty = self.root / "empty"
        empty.mkdir()
        with self.assertRaises(ValueError):
            contract.package_build(empty, self.root / "unused", "a" * 40, "b" * 40, "c" * 64)

    def test_symlinks_are_never_followed(self):
        # Arrange: executed in Linux Docker, without Windows privilege assumptions.
        link = self.root / "link"
        try:
            link.symlink_to(self.dist, target_is_directory=True)
        except OSError:
            self.skipTest("Symlink creation requires platform privileges")
        # Act / Assert
        with self.assertRaises(ValueError):
            contract.package_build(link, self.root / "unused", "a" * 40, "b" * 40, "c" * 64)
        private = self.dist / "assets/linked.js"
        private.symlink_to(self.dist / "index.html")
        with self.assertRaises(ValueError):
            contract.package_build(self.dist, self.root / "unused", "a" * 40, "b" * 40, "c" * 64)
        link.unlink()
        link.symlink_to(self.root / "absent", target_is_directory=True)
        with self.assertRaises(ValueError):
            contract.extract(self.output / "frontend.tar.gz", link, self.manifest)


if __name__ == "__main__":
    unittest.main()
