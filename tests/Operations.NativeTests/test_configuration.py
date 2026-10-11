"""Private database configuration must address the database checked by native operations."""

import unittest

from native_configuration import validate_database_environment

URL = "postgres://mon_kado:synthetic-secret@127.0.0.1:5432/mon_kado?sslmode=disable"


class ConfigurationTests(unittest.TestCase):
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
