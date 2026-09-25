"""Public HTTP-contract fake checks mutation ownership, uncertain responses and cleanup."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from deploy_policy import DeploymentError
from deploy_storage import atomic_write, private_read
from smoke_functional import FunctionalSmoke, LISTS, SESSIONS, credentials, entity_tag, identifier

MEMBER = "11111111-1111-7111-8111-111111111111"
LIST = "22222222-2222-7222-8222-222222222222"
WISH = "33333333-3333-7333-8333-333333333333"
ACCOUNT = {"memberId": MEMBER, "email": "smoke@example.invalid", "password": "synthetic-fixture-only"}
MARKER = "MK820-smoke-" + "a" * 32


class Api:
    """An explicit contract fake, including lost responses after committed writes."""

    def __init__(self):
        self.token = None
        self.csrf = None
        self.list = None
        self.wish = None
        self.version = 1
        self.calls = []
        self.fault = None
        self.lost = None
        self.identity = {"id": MEMBER, "roles": ["Member"]}

    def clear(self):
        self.token = None
        self.csrf = None

    def json(self, method, path, expected=200, payload=None, headers=None):
        self.calls.append((method, path))
        if self.fault == (method, path):
            raise DeploymentError("INJECTED_FAILURE")
        value = self.respond(method, path, payload, headers)
        if self.lost == (method, path):
            self.lost = None
            raise DeploymentError("LOST_RESPONSE")
        return value

    def respond(self, method, path, payload, headers):
        result_headers = {"etag": f'"{self.version:08x}"'}
        if path == "/security/csrf-token":
            return {"token": "synthetic-csrf"}, result_headers
        if path == SESSIONS:
            return {"accessToken": "synthetic-access", "tokenType": "Bearer"}, result_headers
        if path == SESSIONS + "/current":
            return (self.identity if method == "GET" else None), result_headers
        if path == LISTS:
            if method == "GET":
                return [self.list] if self.list else [], result_headers
            self.list = payload | {"id": LIST}
            return self.list, result_headers | {"location": LISTS + "/" + LIST}
        if path == LISTS + "/" + LIST:
            if method == "DELETE":
                self.list = None
                return None, result_headers
            return self.list, result_headers
        if method == "GET" and path == LISTS + "/" + LIST + "/wishes":
            return {"wishes": [self.wish] if self.wish else []}, result_headers
        if method == "POST":
            self.wish = payload | {"id": WISH}
            return self.wish, result_headers | {"location": LISTS + "/" + LIST + "/wishes/" + WISH}
        if method == "PUT":
            if headers != {"If-Match": result_headers["etag"]}:
                raise AssertionError("Wrong optimistic concurrency token")
            self.version += 1
            self.wish = payload | {"id": WISH}
            return self.wish, {"etag": f'"{self.version:08x}"'}
        if method == "DELETE":
            self.wish = None
            return None, result_headers
        return self.wish, result_headers


class FunctionalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.journal = Path(self.temp.name) / "journal.json"
        self.api = Api()
        self.smoke = FunctionalSmoke(self.api, ACCOUNT, self.journal)

    def seed_journal(self, list_id=LIST):
        self.smoke.save({"schemaVersion": 1, "memberId": MEMBER, "marker": MARKER, "listId": list_id})
        self.api.list = {"id": LIST, "name": MARKER, "message": MARKER}

    def test_complete_journey_cleans_resources_and_revokes_session(self):
        self.smoke.run()
        self.assertIsNone(self.api.list)
        self.assertIsNone(self.api.wish)
        self.assertFalse(self.journal.exists())
        self.assertIn(("PUT", LISTS + "/" + LIST + "/wishes/" + WISH), self.api.calls)
        self.assertEqual(("DELETE", SESSIONS + "/current"), self.api.calls[-1])
        self.assertIsNone(self.api.token)

    def test_lost_creation_response_is_reconciled_without_repeated_post(self):
        for lost in (("POST", LISTS), ("POST", LISTS + "/" + LIST + "/wishes")):
            self.api.lost = lost
            with self.subTest(lost=lost), self.assertRaisesRegex(DeploymentError, "LOST_RESPONSE"):
                self.smoke.run()
            self.assertIsNone(self.api.list)
            self.assertIsNone(self.api.wish)
            self.assertFalse(self.journal.exists())

    def test_failed_login_never_mutates_or_cleans_existing_data(self):
        self.seed_journal()
        self.api.fault = ("POST", SESSIONS)
        with self.assertRaises(DeploymentError):
            self.smoke.run()
        self.assertTrue(self.journal.exists())
        self.assertIsNotNone(self.api.list)
        self.assertNotIn(("GET", LISTS), self.api.calls)
        self.assertIsNone(self.api.token)

    def test_wrong_identity_or_admin_cannot_cleanup(self):
        self.seed_journal()
        for identity in ({"id": WISH, "roles": ["Member"]}, {"id": MEMBER, "roles": ["Member", "Administrator"]}):
            self.api.identity = identity
            with self.subTest(identity=identity), self.assertRaisesRegex(DeploymentError, "SMOKE_IDENTITY_REFUSED"):
                self.smoke.run()
            self.assertNotIn(("GET", LISTS), self.api.calls)
            self.assertTrue(self.journal.exists())

    def test_prior_journal_is_cleaned_before_next_creation(self):
        self.seed_journal()
        self.smoke.run()
        deletion = self.api.calls.index(("DELETE", LISTS + "/" + LIST))
        creation = self.api.calls.index(("POST", LISTS))
        self.assertLess(deletion, creation)
        self.assertFalse(self.journal.exists())

    def test_unknown_resources_and_missing_creation_are_preserved(self):
        self.seed_journal()
        self.api.wish = {"id": WISH, "name": "unrelated", "note": "unrelated"}
        with self.assertRaisesRegex(DeploymentError, "SMOKE_CLEANUP_AMBIGUOUS"):
            self.smoke.cleanup()
        self.assertTrue(self.journal.exists())
        self.api.wish = None
        self.api.list["message"] = "modified by someone else"
        with self.assertRaisesRegex(DeploymentError, "SMOKE_CLEANUP_AMBIGUOUS"):
            self.smoke.cleanup()
        self.seed_journal(list_id=None)
        self.api.list = None
        with self.assertRaisesRegex(DeploymentError, "SMOKE_CREATION_UNCONFIRMED"):
            self.smoke.cleanup()
        self.seed_journal()
        self.api.list = None
        self.smoke.cleanup()
        self.assertFalse(self.journal.exists())

    def test_failed_cleanup_retains_journal_but_still_closes_session(self):
        self.api.fault = ("DELETE", LISTS + "/" + LIST)
        with self.assertRaisesRegex(DeploymentError, "INJECTED_FAILURE"):
            self.smoke.run()
        self.assertTrue(self.journal.exists())
        self.assertEqual(("DELETE", SESSIONS + "/current"), self.api.calls[-1])
        self.assertIsNone(self.api.token)

    def test_invalid_journals_are_rejected_without_deletion(self):
        for value in ({}, {"schemaVersion": 1, "memberId": WISH, "marker": MARKER, "listId": LIST},
                      {"schemaVersion": 1, "memberId": MEMBER, "marker": "foreign", "listId": LIST}):
            atomic_write(self.journal, json.dumps(value).encode())
            with self.subTest(value=value), self.assertRaisesRegex(DeploymentError, "SMOKE_JOURNAL_INVALID"):
                self.smoke.cleanup()
        self.assertEqual([], self.api.calls)

    def test_private_credentials_and_identifiers(self):
        for value in (None, "bad"):
            with self.assertRaisesRegex(DeploymentError, "SMOKE_INVALID_IDENTIFIER"):
                identifier(value)
        with self.assertRaisesRegex(DeploymentError, "SMOKE_ETAG_INVALID"):
            entity_tag({"etag": 'W/"1"'})
        target = self.journal.parent / "credentials.json"
        atomic_write(target, json.dumps(ACCOUNT).encode())
        self.assertEqual(ACCOUNT, credentials(target))
        for value in ({}, ACCOUNT | {"password": "short"}):
            atomic_write(target, json.dumps(value).encode())
            with self.assertRaisesRegex(DeploymentError, "SMOKE_CREDENTIALS_INVALID"):
                credentials(target)


if __name__ == "__main__":
    unittest.main()
