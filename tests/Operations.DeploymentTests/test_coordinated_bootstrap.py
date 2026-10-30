"""Tests for the independent fixed-digest staging bootstrap."""

import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_bootstrap import stage


class BootstrapTests(unittest.TestCase):
    def test_stage_when_archive_matches_pin_extracts_and_records_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            archive = base / "bundle.tar.gz"
            name, content = "src/Operations.Deployment/example.py", b"reviewed"
            with tarfile.open(archive, "w:gz") as bundle:
                entry = tarfile.TarInfo(name)
                entry.size = len(content)
                bundle.addfile(entry, io.BytesIO(content))
            manifest = {"archiveSha256": hashlib.sha256(archive.read_bytes()).hexdigest(),
                        "files": {name: hashlib.sha256(content).hexdigest()}}
            encoded = json.dumps(manifest).encode()
            manifest_file = base / "manifest.json"
            manifest_file.write_bytes(encoded)
            target = base / "source"
            with self.assertRaisesRegex(ValueError, "manifest digest mismatch"):
                stage(archive, manifest_file, "0" * 64, target)
            stage(archive, manifest_file, hashlib.sha256(encoded).hexdigest(), target)
            self.assertEqual((target / name).read_bytes(), content)
            self.assertEqual((target / "approved-manifest.json").read_bytes(), encoded)
            self.assertEqual((target / "approved-manifest.json").stat().st_mode & 0o777, 0o600)

    def test_stage_when_inventory_escapes_destination_rejects_before_creation(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            manifest = base / "manifest.json"
            encoded = json.dumps({"files": {"src/Operations.X/../../outside": "a" * 64}}).encode()
            manifest.write_bytes(encoded)
            with self.assertRaisesRegex(ValueError, "invalid inventory"):
                stage(base / "missing", manifest, hashlib.sha256(encoded).hexdigest(), base / "source")
            self.assertFalse((base / "source").exists())
