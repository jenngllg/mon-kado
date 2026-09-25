"""Catalog validation includes source rewriting, EF identity and canonical hashing."""

import contextlib
import io
import runpy
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from release_catalog import canonical, fingerprint, generate, main, validate_catalog


class CatalogTests(unittest.TestCase):
    def test_build_catalog_tracks_both_migration_and_model_sources(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "20260101000000_Initial.cs"
            source.write_bytes(b"migration\n")
            metadata = root / "20260101000000_Initial.Designer.cs"
            metadata.write_bytes(b'[Migration("20260101000000_Initial")]\n')
            snapshot = root / "MonKadoDbContextModelSnapshot.cs"
            snapshot.write_bytes(b"model\n")
            value = generate(root)
            self.assertEqual(value, validate_catalog(value))
            source.write_bytes(b"migration\r\n")
            self.assertEqual(value, generate(root))
            source.write_bytes(b"modified\n")
            self.assertNotEqual(fingerprint(value), fingerprint(generate(root)))
            snapshot.write_bytes(b"changed model")
            self.assertNotEqual(value["modelHash"], generate(root)["modelHash"])
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                main([str(root)])
            self.assertEqual(canonical(generate(root)).decode() + "\n", output.getvalue())
            script = Path(__file__).resolve().parents[2] / "src/Operations.Deployment/release_catalog.py"
            with patch.object(sys, "argv", [str(script), str(root)]), contextlib.redirect_stdout(io.StringIO()):
                runpy.run_path(str(script), run_name="__main__")
            metadata.write_bytes(b'[Migration("20260101000000_Different")]')
            with self.assertRaises(ValueError):
                generate(root)

    def test_rejects_malformed_or_ambiguous_catalogs(self):
        entry = {"id": "20260101000000_Initial", "sha256": "a" * 64}
        base = {"schemaVersion": 1, "modelHash": "b" * 64, "migrations": [entry]}
        invalid = [None, [], {}, base | {"schemaVersion": True}, base | {"modelHash": None},
                   base | {"modelHash": "wrong"}, base | {"migrations": None}, base | {"migrations": []},
                   base | {"migrations": [entry] * 201}, base | {"migrations": [entry, entry]}]
        for item in (None, {}, entry | {"extra": 1}, entry | {"id": None}, entry | {"id": "bad"},
                     entry | {"sha256": None}, entry | {"sha256": "bad"}):
            invalid.append(base | {"migrations": [item]})
        for value in invalid:
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate_catalog(value)
        with self.assertRaisesRegex(ValueError, "INVALID_ARGUMENTS"):
            main([])


if __name__ == "__main__":
    unittest.main()
