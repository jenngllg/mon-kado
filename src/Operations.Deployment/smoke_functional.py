"""A dedicated non-administrator exercises real persistence with strictly scoped cleanup."""

import json
import re
import uuid

from deploy_policy import DeploymentError, ensure
from deploy_storage import atomic_write, private_read, sync_directory

LISTS = "/api/v1/wishlists"
SESSIONS = "/api/v1/auth/sessions"


def identifier(value):
    """Validate IDs before constructing a request path or persisting cleanup evidence."""
    ensure(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}", value) is not None,
           "SMOKE_INVALID_IDENTIFIER")
    return value


def credentials(path):
    """Load a separately provisioned account without storing it in deployment state."""
    value = json.loads(private_read(path, 8192))
    ensure(isinstance(value, dict) and set(value) == {"memberId", "email", "password"}, "SMOKE_CREDENTIALS_INVALID")
    identifier(value["memberId"])
    ensure(isinstance(value["email"], str) and 3 <= len(value["email"]) <= 254
           and isinstance(value["password"], str) and 12 <= len(value["password"]) <= 128, "SMOKE_CREDENTIALS_INVALID")
    return value


def entity_tag(headers):
    """Require the API's strong quoted concurrency token."""
    value = headers.get("etag", "")
    ensure(re.fullmatch(r'"[0-9a-f]{8}"', value) is not None, "SMOKE_ETAG_INVALID")
    return value


class FunctionalSmoke:
    """Never delete resources outside an exact journaled marker and verified member identity."""

    def __init__(self, client, account, journal):
        self.client = client
        self.account = account
        self.journal = journal
        self.identified = False

    def login(self):
        """Respect CSRF and verify the principal through the authenticated public endpoint."""
        self.identified = False
        csrf, _ = self.client.json("GET", "/security/csrf-token")
        self.client.csrf = csrf["token"]
        result, _ = self.client.json("POST", SESSIONS, payload={"email": self.account["email"],
                                                              "password": self.account["password"], "rememberMe": False})
        ensure(result.get("tokenType") == "Bearer" and isinstance(result.get("accessToken"), str), "SMOKE_LOGIN_FAILED")
        self.client.token = result["accessToken"]
        current, _ = self.client.json("GET", SESSIONS + "/current")
        ensure(current.get("id") == self.account["memberId"] and current.get("roles") == ["Member"], "SMOKE_IDENTITY_REFUSED")
        self.identified = True
        csrf, _ = self.client.json("GET", "/security/csrf-token")
        self.client.csrf = csrf["token"]

    def run(self):
        """Run once, preserving uncertain mutations for reconciliation rather than retrying them."""
        try:
            self.login()
            if self.journal.exists():
                self.cleanup()
            marker = "MK820-smoke-" + uuid.uuid4().hex
            self.save({"schemaVersion": 1, "memberId": self.account["memberId"], "marker": marker, "listId": None})
            self.exercise(marker)
        finally:
            try:
                if self.client.token is not None:
                    try:
                        if self.identified and self.journal.exists():
                            self.cleanup()
                    finally:
                        self.client.json("DELETE", SESSIONS + "/current", expected=204)
            finally:
                self.client.clear()

    def save(self, value):
        """Journal intent before sending a mutation; include no credentials or session material."""
        atomic_write(self.journal, json.dumps(value, sort_keys=True).encode())

    def exercise(self, marker):
        """Create, read and update synthetic resources through the unchanged public API."""
        value, headers = self.client.json("POST", LISTS, expected=201,
                                          payload={"name": marker, "occasion": "other", "eventDate": None, "message": marker})
        list_id = identifier(value["id"])
        path = LISTS + "/" + list_id
        ensure(value.get("name") == marker and headers.get("location") in (path, "https://api.monkado.fr" + path),
               "SMOKE_CONTRACT_FAILED")
        entity_tag(headers)
        self.save({"schemaVersion": 1, "memberId": self.account["memberId"], "marker": marker, "listId": list_id})
        found, _ = self.client.json("GET", path)
        ensure(found.get("id") == list_id and found.get("message") == marker, "SMOKE_CONTRACT_FAILED")
        request = {"name": marker, "note": marker, "url": None, "price": None, "quantity": 1}
        wish, headers = self.client.json("POST", path + "/wishes", expected=201, payload=request)
        wish_path = path + "/wishes/" + identifier(wish["id"])
        ensure(wish.get("name") == marker and headers.get("location") in (wish_path, "https://api.monkado.fr" + wish_path),
               "SMOKE_CONTRACT_FAILED")
        original = entity_tag(headers)
        found, headers = self.client.json("GET", wish_path)
        ensure(found.get("id") == wish["id"] and found.get("quantity") == 1 and entity_tag(headers) == original,
               "SMOKE_CONTRACT_FAILED")
        changed, headers = self.client.json("PUT", wish_path, payload=request | {"quantity": 2}, headers={"If-Match": original})
        ensure(changed.get("id") == wish["id"] and changed.get("quantity") == 2 and entity_tag(headers) != original,
               "SMOKE_CONTRACT_FAILED")
        found, _ = self.client.json("GET", wish_path)
        ensure(found.get("quantity") == 2 and found.get("note") == marker, "SMOKE_CONTRACT_FAILED")

    def cleanup(self):
        """Reconcile a lost creation response, then remove only the known synthetic resources."""
        value = json.loads(private_read(self.journal, 4096))
        ensure(isinstance(value, dict) and set(value) == {"schemaVersion", "memberId", "marker", "listId"}
               and type(value["schemaVersion"]) is int and value["schemaVersion"] == 1
               and value["memberId"] == self.account["memberId"], "SMOKE_JOURNAL_INVALID")
        marker = value["marker"]
        ensure(isinstance(marker, str) and re.fullmatch(r"MK820-smoke-[0-9a-f]{32}", marker) is not None, "SMOKE_JOURNAL_INVALID")
        lists, _ = self.client.json("GET", LISTS)
        ensure(isinstance(lists, list), "SMOKE_CONTRACT_FAILED")
        matches = [item for item in lists if item.get("name") == marker and item.get("message") == marker]
        ensure(len(matches) <= 1, "SMOKE_CLEANUP_AMBIGUOUS")
        if not matches:
            ensure(value["listId"] is not None, "SMOKE_CREATION_UNCONFIRMED")
            ensure(not any(item.get("id") == value["listId"] for item in lists), "SMOKE_CLEANUP_AMBIGUOUS")
        else:
            list_id = identifier(matches[0]["id"])
            ensure(value["listId"] in (None, list_id), "SMOKE_CLEANUP_AMBIGUOUS")
            value["listId"] = list_id
            self.save(value)
            self.delete_list(list_id, marker)
        self.journal.unlink()
        sync_directory(self.journal.parent)

    def delete_list(self, list_id, marker):
        """Refuse unexpected contents instead of cascading deletion of unknown resources."""
        path = LISTS + "/" + list_id
        wishes, _ = self.client.json("GET", path + "/wishes")
        items = wishes.get("wishes")
        ensure(isinstance(items, list) and len(items) <= 1, "SMOKE_CLEANUP_AMBIGUOUS")
        for wish in items:
            ensure(wish.get("name") == marker and wish.get("note") == marker, "SMOKE_CLEANUP_AMBIGUOUS")
            wish_path = path + "/wishes/" + identifier(wish["id"])
            found, headers = self.client.json("GET", wish_path)
            ensure(found.get("name") == marker and found.get("note") == marker, "SMOKE_CLEANUP_AMBIGUOUS")
            self.client.json("DELETE", wish_path, expected=204, headers={"If-Match": entity_tag(headers)})
        found, headers = self.client.json("GET", path)
        ensure(found.get("name") == marker and found.get("message") == marker, "SMOKE_CLEANUP_AMBIGUOUS")
        self.client.json("DELETE", path, expected=204, headers={"If-Match": entity_tag(headers)})
