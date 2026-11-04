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

    def test_each_committed_prefix_can_resume_without_replaying_or_reversing_sql(self):
        expanded = identifiers(self.new["migrationCatalog"])
        for count in (29,30):
            with self.subTest(count=count):
                self.setUp()
                self.history[:] = expanded[:count]
                self.engine.deploy(self.new)
                self.assertEqual(expanded,self.history)
                self.runtime.migrate.assert_called_once_with(self.new)

    def test_failed_partial_expansion_restores_compatible_binaries_and_keeps_commits(self):
        from deploy_policy import DeploymentError
        expanded = identifiers(self.new["migrationCatalog"])
        def partial(_):
            self.history[:] = expanded[:30]
            raise DeploymentError("MIGRATION_ACKNOWLEDGEMENT_LOST")
        self.runtime.migrate.side_effect = partial
        with self.assertRaisesRegex(DeploymentError,"PUBLICATION_ROLLED_BACK"):
            self.engine.deploy(self.new)
        self.assertEqual(self.old,self.state["current"])
        self.assertEqual(expanded[:30],self.history)
        self.runtime.start.assert_called_with(self.old)
        with self.assertRaisesRegex(DeploymentError,"PUBLICATION_REJECTED"):
            self.engine.deploy(self.new)
        # Recovery requires a separately approved publication, preserving the existing rejection guard.
        replacement = copy.deepcopy(self.new)
        replacement["publicationId"] = "789-1"
        for binary in replacement["binaries"].values():
            binary["url"] = binary["url"].replace("456-1", "789-1")
        self.runtime.migrate.side_effect = lambda _: self.history.__setitem__(slice(None),expanded)
        self.engine.deploy(replacement)
        self.assertEqual(expanded,self.history)

    def test_foreign_or_reordered_partial_histories_are_not_accepted(self):
        from deploy_policy import DeploymentError
        history = identifiers(self.new["migrationCatalog"])[:30]
        for invalid in (history+['999_unknown.sql'],history[:28]+list(reversed(history[28:]))):
            with self.subTest(history=invalid),self.assertRaisesRegex(DeploymentError,"DATABASE_HISTORY_DIVERGED"):
                self.engine.policy(self.old,self.new,invalid)
