"""Only the reviewed notification expansion may keep the old API serving SQL changes."""

import copy
import hashlib
from pathlib import Path
import unittest
from unittest.mock import Mock, patch

import coordinated_cli as cli
from deploy_policy import DeploymentError, initial_state
from deploy_runtime import Runtime
from release_catalog import fingerprint, generate, identifiers
from test_engine import release


def notification_releases():
    root = Path(__file__).resolve().parents[2]
    directory = root / "internal/persistence/migrations"
    expanded = generate(directory)
    previous = copy.deepcopy(expanded)
    previous["migrations"] = previous["migrations"][:-1]
    model = hashlib.sha256()
    for item in previous["migrations"]:
        body = (directory / item["id"]).read_bytes().replace(b"\r\n", b"\n")
        model.update(item["id"].encode() + b"\0" + body + b"\0")
    previous["modelHash"] = model.hexdigest()
    old, new = release("a"), release("b")
    for value, catalog in ((old, previous), (new, expanded)):
        value.update(migrationCatalog=catalog, migrationHash=fingerprint(catalog))
    return old, new


class OnlineNotificationMigrationTests(unittest.TestCase):
    def setUp(self):
        self.old, self.new = notification_releases()
        self.state = initial_state() | {"current": self.old, "phase": "succeeded"}
        self.store = Mock()
        self.store.load.return_value = self.state
        self.history = identifiers(self.old["migrationCatalog"])
        self.runtime = Mock()
        self.runtime.history.side_effect = lambda: list(self.history)
        self.runtime.preflight.return_value = {"infrastructure": {}, "frontend": None}
        self.runtime.smoke.return_value = {"technical": True, "functional": True}
        self.runtime.migrate.side_effect = lambda _: self.history.extend(identifiers(self.new["migrationCatalog"])[-1:])
        self.engine = cli.NotificationOnlineEngine(self.store, self.runtime, lambda: "2026-11-03T00:00:00+00:00")

    def test_exact_sql_catalogs_are_pinned_and_other_changes_remain_blocked(self):
        self.assertTrue(cli.notification_expansion(self.old, self.new))
        for field in ("modelHash", "sql"):
            changed = copy.deepcopy(self.new)
            if field == "sql":
                changed["migrationCatalog"]["migrations"][-1]["sha256"] = "f" * 64
            else:
                changed["migrationCatalog"]["modelHash"] = "f" * 64
            self.assertFalse(cli.online_catalog_compatible(self.old, changed))
        sql = (Path(__file__).resolve().parents[2] / "internal/persistence/migrations/027_member_notifications.sql").read_text()
        self.assertIn("SET LOCAL lock_timeout = '2s'", sql)
        self.assertIn("SET LOCAL statement_timeout = '30s'", sql)
        self.assertNotIn("DROP ", sql)

    def test_success_runs_the_migration_once_and_persists_the_expanded_history(self):
        result = self.engine.deploy(self.new)
        self.assertEqual("succeeded", result["phase"])
        self.assertEqual(self.new, result["current"])
        self.assertEqual(identifiers(self.new["migrationCatalog"]), self.history)
        self.engine.deploy(self.new)
        self.runtime.migrate.assert_called_once_with(self.new)

    def test_failed_candidate_restores_old_binaries_without_reversing_committed_sql(self):
        self.runtime.start.side_effect = [DeploymentError("ONLINE_API_NOT_READY"), None]
        with self.assertRaisesRegex(DeploymentError, "PUBLICATION_ROLLED_BACK"):
            self.engine.deploy(self.new)
        self.assertEqual(self.old, self.state["current"])
        self.assertEqual("rolledBack", self.state["phase"])
        self.assertEqual(identifiers(self.new["migrationCatalog"]), self.history)
        self.assertEqual([self.new, self.old], [call.args[0] for call in self.runtime.start.call_args_list])

    def test_lost_migration_acknowledgement_reconciles_history_before_binary_rollback(self):
        def commit_then_fail(_):
            self.history[:] = identifiers(self.new["migrationCatalog"])
            raise DeploymentError("MIGRATION_ACKNOWLEDGEMENT_LOST")
        self.runtime.migrate.side_effect = commit_then_fail
        with self.assertRaisesRegex(DeploymentError, "PUBLICATION_ROLLED_BACK"):
            self.engine.deploy(self.new)
        self.assertEqual(self.old, self.state["current"])
        self.assertEqual(identifiers(self.new["migrationCatalog"]), self.history)

    def test_committed_expansion_is_not_replayed_after_interruption(self):
        self.history[:] = identifiers(self.new["migrationCatalog"])
        self.engine.deploy(self.new)
        self.runtime.migrate.assert_not_called()

    def test_divergent_history_and_missing_rollback_approval_are_refused(self):
        for history, approved in (([], True), (self.history, False)):
            with self.subTest(history=history, approved=approved), self.assertRaises(DeploymentError):
                self.engine.policy(self.old, self.new | {"rollbackAllowed": approved}, history)

    def test_runtime_only_executes_the_reviewed_catalog_and_keeps_the_proxy_running(self):
        runtime = cli.NotificationOnlineRuntime(runner=Mock())
        with patch.object(Runtime, "migrate") as migrate:
            with self.assertRaisesRegex(DeploymentError, "ONLINE_SCHEMA_REVIEW_REQUIRED"):
                runtime.migrate(self.old)
            migrate.assert_not_called()
            runtime.migrate(self.new)
            migrate.assert_called_once_with(runtime, self.new)
        runtime.container = Mock(return_value={"running": True})
        runtime.stop()
        runtime.run.assert_not_called()

