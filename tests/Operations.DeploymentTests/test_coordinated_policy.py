"""Verify complete pair and backup proofs without contacting production."""

import copy
from datetime import datetime, timedelta, timezone
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Deployment", "src/Operations.Frontend", "deployments/production"):
    sys.path.insert(0, str(ROOT / path))

from coordinated_policy import validate_backup, validate_intent, validate_pairs
from deploy_policy import DeploymentError
from release_catalog import fingerprint


CONFIG = "c" * 64
SNAPSHOT = "d" * 64
NOW = datetime(2026, 10, 30, 13, tzinfo=timezone.utc)


def pair(revision, frontend, publication):
    catalog = {"schemaVersion": 2, "modelHash": "d" * 64,
               "migrations": [{"id": "001_initial.sql", "sha256": "b" * 64}]}
    backend = {"schemaVersion": 3, "publicationId": publication,
               "revision": revision * 40, "configurationHash": CONFIG,
               "binaries": {name: {"sha256": revision * 64, "size": 1024,
                    "url": f"https://github.com/jenngllg/mon-kado/releases/download/backend-{revision * 40}-{publication}/monkado-{name}-linux-amd64"}
                    for name in ("api", "worker", "migrate")},
               "migrationCatalog": catalog, "migrationHash": fingerprint(catalog), "rollbackAllowed": True}
    front = {"schemaVersion": 2, "revision": frontend * 40, "backendRevision": revision * 40,
             "configurationHash": CONFIG, "apiOrigin": "https://api.monkado.fr",
             "archiveSha256": "f" * 64, "googleEnabled": True}
    return {"backend": backend, "frontend": front}


def candidate_pair():
    value = pair("b", "f", "456-1")
    value["proof"] = {"schemaVersion": 1, "frontendRevision": "f" * 40, "backendRevision": "b" * 40,
                      "configurationHash": CONFIG, "openapiSha256": "d" * 64}
    return value


def backup():
    capture = (NOW - timedelta(minutes=5)).isoformat()
    return {"snapshotId": SNAPSHOT, "error": None, "lastCapture": capture,
            "lastRemoteCapture": capture, "lastRemoteSuccess": (NOW - timedelta(minutes=3)).isoformat(),
            "lastIntegrityCheck": (NOW - timedelta(minutes=2)).isoformat()}


class CoordinatedPolicyTests(unittest.TestCase):
    def test_validate_pairs_when_each_manifest_matches_its_backend_accepts_unchanged_schema(self):
        previous, candidate = pair("a", "e", "123-1"), candidate_pair()
        validate_pairs(previous, candidate, CONFIG, ["001_initial.sql"])

    def test_validate_pairs_when_evidence_is_wrong_rejects_the_transition(self):
        previous, candidate = pair("a", "e", "123-1"), candidate_pair()
        variants = []
        bad = copy.deepcopy(candidate); bad["frontend"]["backendRevision"] = "a" * 40; variants.append(bad)
        bad = copy.deepcopy(candidate); bad["backend"]["rollbackAllowed"] = False; variants.append(bad)
        bad = copy.deepcopy(candidate); bad["backend"]["binaries"]["api"]["sha256"] = "invalid"; variants.append(bad)
        bad = copy.deepcopy(candidate); bad["backend"]["migrationCatalog"]["migrations"].append({"id": "002_next.sql", "sha256": "e" * 64}); bad["backend"]["migrationHash"] = fingerprint(bad["backend"]["migrationCatalog"]); variants.append(bad)
        bad = copy.deepcopy(candidate); bad["frontend"]["configurationHash"] = "0" * 64; variants.append(bad)
        bad = copy.deepcopy(candidate); bad["proof"]["backendRevision"] = "a" * 40; variants.append(bad)
        bad = copy.deepcopy(candidate); bad["proof"]["openapiSha256"] = "invalid"; variants.append(bad)
        for value in variants:
            with self.subTest(value=value), self.assertRaises((ValueError, DeploymentError)):
                validate_pairs(previous, value, CONFIG, ["001_initial.sql"])
        with self.assertRaises(DeploymentError):
            validate_pairs(previous, candidate, CONFIG, [])

    def test_validate_backup_when_fresh_and_remote_checked_accepts_snapshot(self):
        validate_backup(backup(), SNAPSHOT, NOW, NOW - timedelta(minutes=10))

    def test_validate_intent_when_tampered_rejects_recovery(self):
        intent = {"version": 1, "phase": "recovering", "previous": pair("a", "e", "123-1"),
                  "candidate": candidate_pair(), "backup": SNAPSHOT, "frontendBackup": None, "toolDigest": "a" * 64}
        validate_intent(intent, CONFIG, ["001_initial.sql"], "a" * 64)
        for change in ({"version": True}, {"phase": "ready"}, {"backup": "latest"}, {"extra": 1}):
            with self.subTest(change=change), self.assertRaises(DeploymentError):
                validate_intent(intent | change, CONFIG, ["001_initial.sql"], "a" * 64)
        with self.assertRaises(DeploymentError):
            validate_intent(intent | {"phase": "installingFrontend"}, CONFIG, ["001_initial.sql"], "a" * 64)
        with self.assertRaises(DeploymentError):
            validate_intent(intent | {"phase": "installingFrontend", "frontendBackup": SNAPSHOT}, CONFIG, ["001_initial.sql"], "a" * 64)
        with self.assertRaises(DeploymentError):
            validate_intent(intent, CONFIG, [], "a" * 64)
        with self.assertRaises(DeploymentError):
            validate_intent(intent, CONFIG, ["001_initial.sql"], "b" * 64)

    def test_validate_backup_when_evidence_is_missing_stale_or_inconsistent_rejects(self):
        original = backup()
        changes = [{"snapshotId": "e" * 64}, {"error": "FAILED"}, {"lastRemoteCapture": "other"},
                   {"lastIntegrityCheck": (NOW - timedelta(minutes=6)).isoformat()},
                   {"lastRemoteSuccess": (NOW + timedelta(minutes=1)).isoformat()},
                   {"lastCapture": "invalid"}, {"lastCapture": "2026-10-30T12:55:00"}]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(DeploymentError):
                validate_backup(original | change, SNAPSHOT, NOW, NOW - timedelta(minutes=10))
        with self.assertRaises(DeploymentError):
            validate_backup(original, SNAPSHOT, NOW, NOW - timedelta(minutes=1))
        with self.assertRaises(DeploymentError):
            validate_backup(original, SNAPSHOT, NOW + timedelta(hours=2), NOW - timedelta(minutes=10))


if __name__ == "__main__":
    unittest.main()
