"""Private database configuration must address the database checked by native operations."""

import base64
import unittest

from native_configuration import validate_database_environment, validate_runtime_keys

URL = "postgres://mon_kado:synthetic-secret@127.0.0.1:5432/mon_kado?sslmode=disable"


class ConfigurationTests(unittest.TestCase):
    def test_both_stable_runtime_keys_are_mandatory_and_duplicate_assignments_are_rejected(self):
        encoded = base64.b64encode(bytes(range(32))).decode()
        valid = ("JWT_SIGNING_KEY=" + encoded + "\nDATA_ENCRYPTION_KEY=" + encoded + "\n").encode()
        self.assertIsNone(validate_runtime_keys(valid))
        for invalid in (b"", b"JWT_SIGNING_KEY=private", valid + b"DATA_ENCRYPTION_KEY=private\n",
                        valid.replace(encoded.encode(), b"AQ==")):
            with self.subTest(invalid=invalid), self.assertRaisesRegex(ValueError, "^NATIVE_RUNTIME_KEYS_INVALID$"):
                validate_runtime_keys(invalid)

    def test_canonical_and_quoted_systemd_assignments_are_accepted_without_returning_credentials(self):
        for value in (URL, '"' + URL + '"', "'" + URL + "'"):
            with self.subTest(value=value):
                self.assertIsNone(validate_database_environment(("# private file\nDATABASE_URL=" + value + "\nAPP_ENV=production\n").encode()))

    def test_alternate_targets_and_ambiguous_assignments_are_rejected_without_secret_diagnostics(self):
        invalid = [URL.replace("127.0.0.1", "example.com"), URL.replace("5432", "55432"), URL.replace("/mon_kado?", "/other?"),
                   URL.replace("mon_kado:", "postgres:"), URL + "&host=other", URL + "&sslmode=disable", URL + "#fragment",
                   URL.replace("synthetic-secret", ""), URL.replace("?sslmode=disable", ""), URL + " private"]
        for value in invalid:
            with self.subTest(value=value), self.assertRaises(ValueError) as result:
                validate_database_environment(("DATABASE_URL=" + value + "\n").encode())
            self.assertNotIn("synthetic-secret", str(result.exception))
        for value in (b"", ("DATABASE_URL=" + URL + "\nDATABASE_URL=" + URL).encode(), b"export DATABASE_URL=private"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate_database_environment(value)
