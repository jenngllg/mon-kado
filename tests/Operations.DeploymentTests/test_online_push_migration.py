"""Pin the reviewed 028 -> 031 push expansion and its rollback guarantees."""
import copy
import hashlib
from pathlib import Path
import tempfile
import shutil
import unittest

import coordinated_cli as cli
from release_catalog import generate, fingerprint, identifiers
from test_engine import release
from test_online_notification_migration import OnlineNotificationMigrationTests, preference_releases


def push_releases():
    root = Path(__file__).resolve().parents[2]
    _, old = preference_releases()
    with tempfile.TemporaryDirectory() as temporary:
        directory = Path(temporary)
        for source in (root / "internal/persistence/migrations").glob("*.sql"):
            if source.name <= "028_notification_preferences.sql":
                shutil.copyfile(source, directory / source.name)
        for source in (root / "tests/fixtures/reviewed-push-sql").glob("*.sql"):
            shutil.copyfile(source, directory / source.name)
        catalog = generate(directory)
    return old, release("d") | {"migrationCatalog":catalog,"migrationHash":fingerprint(catalog)}


class OnlinePushMigrationTests(OnlineNotificationMigrationTests):
    def setUp(self):
        super().setUp()
        self.old, self.new = push_releases()
        self.state["current"] = self.old
        self.history[:] = identifiers(self.old["migrationCatalog"])
        self.runtime.migrate.side_effect = lambda _: self.history.__setitem__(slice(None), identifiers(self.new["migrationCatalog"]))

    def test_review_is_bound_to_exact_additive_sql_and_any_shipped_sources(self):
        root = Path(__file__).resolve().parents[2]
        self.assertEqual(cli.PUSH_EXPANDED_CATALOG, fingerprint(self.new["migrationCatalog"]))
        self.assertEqual(28, len(identifiers(self.old["migrationCatalog"])))
        self.assertEqual(31, len(identifiers(self.new["migrationCatalog"])))
        for source in (root / "tests/fixtures/reviewed-push-sql").glob("*.sql"):
            sql = source.read_text()
            self.assertIn("SET LOCAL lock_timeout = '2s'",sql)
            self.assertIn("SET LOCAL statement_timeout = '30s'",sql)
            for forbidden in ("DROP ","DELETE FROM", "TRUNCATE "):
                self.assertNotIn(forbidden,sql)
            shipped = root / "internal/persistence/migrations" / source.name
            if shipped.exists():
                self.assertEqual(source.read_bytes().replace(b"\r\n",b"\n"),shipped.read_bytes().replace(b"\r\n",b"\n"))
        # Skipping the compatibility release/catalog, or changing older SQL, is never approved.
        changed = copy.deepcopy(self.old)
        changed["migrationCatalog"]["migrations"][0]["sha256"] = "e"*64
        self.assertFalse(cli.notification_expansion(changed,self.new))
