"""Exercise the security boundary between wildcard metadata and actual edge dependencies."""
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
from check_caddy_scan import verify

CONFIG = json.dumps({"config": {"scanner_name": "govulncheck", "scan_mode": "binary", "scan_level": "symbol"}}) + "\n"


class CaddyScanTests(unittest.TestCase):
    def test_verify_whenUnimportedOpenPGPWildcard_isQualified(self):
        # Arrange
        report = json.dumps({"finding": {"osv": "GO-2026-5932", "trace": [{"package": "golang.org/x/crypto/openpgp/armor", "function": "*"}]}})
        # Act / Assert
        self.assertEqual(verify(CONFIG + report, {"net/http", "golang.org/x/crypto/ocsp"}), ["GO-2026-5932"])

    def test_verify_whenScannerSplitsWildcardAtPackageDot_isQualified(self):
        # Arrange
        report = json.dumps({"finding": {"osv": "GO-2026-5932", "trace": [{"package": "golang.org/x/crypto/openpgp/armor", "receiver": "golang", "function": "org/x/crypto/openpgp/armor/*"}]}})
        # Act / Assert
        self.assertEqual(verify(CONFIG + report, {"net/http"}), ["GO-2026-5932"])

    def test_verify_whenFindingCanAffectExecutable_rejects(self):
        for name, advisory, frame, graph in (
            ("http2", "GO-2026-6617", {"package": "net/http", "function": "Serve"}, {"net/http"}),
            ("openpgpImported", "GO-2026-5932", {"package": "golang.org/x/crypto/openpgp", "function": "*"}, {"golang.org/x/crypto/openpgp/armor"}),
            ("concreteSymbol", "GO-2026-5932", {"package": "golang.org/x/crypto/openpgp", "function": "ReadMessage"}, {"net/http"}),
            ("unexpectedPackage", "GO-2026-5932", {"package": "golang.org/x/crypto/ocsp", "function": "*"}, {"net/http"}),
        ):
            with self.subTest(name=name):
                # Arrange
                report = json.dumps({"finding": {"osv": advisory, "trace": [frame]}})
                # Act / Assert
                with self.assertRaises(ValueError):
                    verify(CONFIG + report, graph)

    def test_verify_whenNoFindings_accepts(self):
        # Arrange / Act / Assert
        self.assertEqual(verify(CONFIG + json.dumps({"progress": {"message": "Complete"}}), {"net/http"}), [])

    def test_verify_whenReportMalformed_rejects(self):
        # Arrange / Act / Assert
        with self.assertRaises(ValueError):
            verify("not json", {"net/http"})
        with self.assertRaises(ValueError):
            verify("", {"net/http"})
