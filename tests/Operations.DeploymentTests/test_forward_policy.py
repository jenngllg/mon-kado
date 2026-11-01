"""Forward policy preserves old approvals and refuses rewritten SQL or rollback."""

import copy
import unittest

from test_coordinated_policy import pair, candidate_pair, CONFIG
from coordinated_policy import validate_forward_pairs, validate_intent
from deploy_policy import DeploymentError
from release_catalog import fingerprint


def pairs():
    old, new = pair("a", "e", "123-1"), candidate_pair()
    old["frontend"]["backendRevision"] = "9" * 40
    old["frontend"]["configurationHash"] = "8" * 64
    old["proof"] = {"schemaVersion": 1, "frontendRevision": "e" * 40, "backendRevision": "9" * 40,
                    "configurationHash": "8" * 64, "openapiSha256": "d" * 64}
    catalog = new["backend"]["migrationCatalog"]
    catalog["migrations"].append({"id": "002_boolean.sql", "sha256": "3" * 64})
    catalog["modelHash"] = "4" * 64
    new["backend"]["migrationHash"] = fingerprint(catalog)
    new["backend"]["rollbackAllowed"] = False
    return old, new


class ForwardPolicyTests(unittest.TestCase):
    def test_original_approval_and_append_only_history_are_retained(self):
        old, new = pairs()
        before = copy.deepcopy((old, new))
        validate_forward_pairs(old, new, CONFIG, ["001_initial.sql"])
        validate_forward_pairs(old, new, CONFIG, ["001_initial.sql", "002_boolean.sql"])
        self.assertEqual(before, (old, new))

    def test_unapproved_bindings_rewritten_migrations_and_divergent_history_fail(self):
        changes = [
            lambda old, new: new["backend"].update(rollbackAllowed=True),
            lambda old, new: new["proof"].update(backendRevision="7" * 40),
            lambda old, new: old["proof"].update(frontendRevision="7" * 40),
            lambda old, new: new["frontend"].update(configurationHash="7" * 64),
            lambda old, new: new["backend"]["migrationCatalog"]["migrations"][0].update(sha256="7" * 64),
            lambda old, new: new["backend"]["migrationCatalog"].update(migrations=[]),
        ]
        for change in changes:
            with self.subTest(change=change):
                old, new = pairs()
                change(old, new)
                with self.assertRaises((DeploymentError, ValueError)):
                    validate_forward_pairs(old, new, CONFIG, ["001_initial.sql"])
        for history in ([], ["unrelated.sql"], ["001_initial.sql", "002_boolean.sql", "003_foreign.sql"]):
            with self.assertRaises(DeploymentError):
                validate_forward_pairs(*pairs(), CONFIG, history)

    def test_forward_intent_retains_candidate_and_requires_frontend_backup_before_switch(self):
        old, new = pairs()
        intent = {"version": 2, "phase": "prepared", "previous": old, "candidate": new,
                  "backup": "1" * 64, "frontendBackup": None, "toolDigest": "2" * 64}
        validate_intent(intent, CONFIG, ["001_initial.sql"], "2" * 64)
        intent["phase"] = "installingFrontend"
        with self.assertRaises(DeploymentError):
            validate_intent(intent, CONFIG, ["001_initial.sql", "002_boolean.sql"], "2" * 64)
        intent["frontendBackup"] = "3" * 64
        validate_intent(intent, CONFIG, ["001_initial.sql", "002_boolean.sql"], "2" * 64)
        with self.assertRaises(DeploymentError):
            validate_intent(intent, CONFIG, ["001_initial.sql"], "4" * 64)


if __name__ == "__main__":
    unittest.main()
