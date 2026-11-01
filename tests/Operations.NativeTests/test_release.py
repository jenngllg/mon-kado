"""Native approval contracts reject mutable artifacts and rewritten SQL before maintenance."""

import copy
import unittest

from deploy_policy import decision, dotenv, DeploymentError
from release_catalog import fingerprint, identifiers
from release_manifest import validate, from_release


def release(revision="a", publication="123-1"):
    catalog = {"schemaVersion": 2, "migrations": [{"id": "001_baseline.sql", "sha256": "b" * 64}], "modelHash": "c" * 64}
    return {"schemaVersion": 3, "revision": revision * 40, "configurationHash": "d" * 64,
            "publicationId": publication, "migrationCatalog": catalog, "migrationHash": fingerprint(catalog),
            "rollbackAllowed": True, "binaries": {component: {
                "url": f'https://github.com/jenngllg/mon-kado/releases/download/backend-{revision * 40}-{publication}/monkado-{component}-linux-amd64',
                "sha256": "e" * 64, "size": 1048576} for component in ("api", "worker", "migrate")}}


class ReleaseTests(unittest.TestCase):
    def test_complete_native_publication_binds_all_three_binaries_and_sql_catalog(self):
        value = release()
        self.assertEqual(value, validate(value, "d" * 64))
        self.assertEqual({"migrate": True, "rollback": False}, decision(None, value, []))
        self.assertEqual({"migrate": False, "rollback": True}, decision(value, release("b", "456-1"), identifiers(value["migrationCatalog"])))
        marker = dotenv(value)
        self.assertIn("API_SHA256=" + "e" * 64 + "\n", marker)
        self.assertIn("APP_VERSION=" + "a" * 40 + "\n", marker)
        self.assertNotIn("IMAGE", marker)

    def test_unapproved_ambiguous_or_mutable_artifacts_are_rejected(self):
        mutations = [lambda value: value["binaries"].pop("migrate"),
                     lambda value: value["binaries"]["api"].update(url="https://github.com/jenngllg/mon-kado/releases/latest/download/api"),
                     lambda value: value["binaries"]["worker"].update(url=value["binaries"]["api"]["url"]),
                     lambda value: value["binaries"]["api"].update(sha256="invalid"),
                     lambda value: value["binaries"]["api"].update(size=True),
                     lambda value: value["binaries"]["api"].update(size=0),
                     lambda value: value["binaries"]["api"].update(size=64 * 1024 ** 2 + 1),
                     lambda value: value["binaries"]["api"].update(extra="arbitrary instruction"),
                     lambda value: value.update(rollbackAllowed="true"),
                     lambda value: value.update(migrationHash="f" * 64),
                     lambda value: value.update(extra="arbitrary instruction")]
        for mutate in mutations:
            value = release()
            mutate(value)
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate(value, "d" * 64)
        with self.assertRaises(ValueError):
            validate(release(), "wrong-installed-configuration")

    def test_catalog_rewrites_divergent_history_and_unapproved_rollbacks_fail_before_effects(self):
        current = release()
        candidate = release("b", "456-1")
        for mutate, code in ((lambda value: value["migrationCatalog"]["migrations"][0].update(sha256="f" * 64), "MIGRATION_HISTORY_REWRITTEN"),
                             (lambda value: value["migrationCatalog"].update(modelHash="f" * 64), "MODEL_CHANGED_WITHOUT_MIGRATION")):
            altered = copy.deepcopy(candidate)
            mutate(altered)
            altered["migrationHash"] = fingerprint(altered["migrationCatalog"])
            with self.subTest(code=code), self.assertRaisesRegex(DeploymentError, code):
                decision(current, altered, identifiers(current["migrationCatalog"]))
        with self.assertRaisesRegex(DeploymentError, "DATABASE_HISTORY_DIVERGED"):
            decision(current, candidate, [])
        self.assertFalse(decision(current, candidate | {"rollbackAllowed": False}, identifiers(current["migrationCatalog"]))["rollback"])

    def test_approval_channel_never_accepts_draft_or_alternate_release(self):
        import json
        pointer = {"tag_name": "backend-production", "draft": False, "prerelease": False, "body": json.dumps(release())}
        self.assertEqual(release(), from_release(pointer, "d" * 64))
        for invalid in (pointer | {"draft": True}, pointer | {"prerelease": True}, pointer | {"tag_name": "latest"}):
            with self.assertRaises(ValueError):
                from_release(invalid, "d" * 64)


if __name__ == "__main__":
    unittest.main()
