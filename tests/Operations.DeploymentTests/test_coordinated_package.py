"""Verify archive determinism and strict extraction of reviewed tool bytes."""

import copy
import gzip
import hashlib
import io
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_integrity import FILES
from coordinated_package import extract, package
from deploy_policy import DeploymentError


class CoordinatedPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "source"; self.source.mkdir()
        for name in FILES:
            target = self.source / name; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("reviewed " + name, encoding="utf-8")
        self.manifest = package(self.source, self.root / "first", "a" * 40, "b" * 64)

    def test_package_when_repeated_is_byte_deterministic_and_extracts_exact_inventory(self):
        other = package(self.source, self.root / "second", "a" * 40, "b" * 64)
        self.assertEqual(self.manifest, other)
        self.assertEqual((self.root / "first/coordinator.tar.gz").read_bytes(), (self.root / "second/coordinator.tar.gz").read_bytes())
        extract(self.root / "first/coordinator.tar.gz", self.root / "extracted", self.manifest, "b" * 64)
        for name in FILES:
            self.assertEqual((self.root / "extracted" / name).read_bytes(), (self.source / name).read_bytes())

    def test_extract_when_archive_bytes_change_refuses_before_creating_output(self):
        archive = self.root / "first/coordinator.tar.gz"
        archive.write_bytes(archive.read_bytes() + b"changed")
        with self.assertRaisesRegex(DeploymentError, "TOOL_ARCHIVE_CHANGED"):
            extract(archive, self.root / "extracted", self.manifest, "b" * 64)
        self.assertFalse((self.root / "extracted").exists())

    def test_extract_when_archive_contains_an_unexpected_path_refuses_even_with_matching_digest(self):
        archive = self.root / "malicious.tar.gz"
        with archive.open("wb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w") as output:
                item = tarfile.TarInfo("../../outside"); item.size = 1
                output.addfile(item, io.BytesIO(b"x"))
        manifest = copy.deepcopy(self.manifest)
        manifest["archiveSha256"] = hashlib.sha256(archive.read_bytes()).hexdigest()
        with self.assertRaisesRegex(DeploymentError, "TOOL_ARCHIVE_INVALID"):
            extract(archive, self.root / "extracted", manifest, "b" * 64)
        self.assertFalse((self.root / "outside").exists())


if __name__ == "__main__":
    unittest.main()
