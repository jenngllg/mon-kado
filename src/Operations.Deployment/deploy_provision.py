"""Interactive handoff of an existing dedicated member, never creation or secret rotation."""

import getpass
import json
import sys

from deploy_policy import ensure
from deploy_storage import atomic_write
from smoke_functional import FunctionalSmoke, identifier


def provision(settings, state, client_factory, read=getpass.getpass, terminal=None):
    """Validate a manually registered account before atomically installing its private file."""
    terminal = sys.stdin.isatty() if terminal is None else terminal
    ensure(terminal, "INTERACTIVE_TERMINAL_REQUIRED")
    target = settings / "deployment-smoke.json"
    ensure(not target.exists() and not target.is_symlink(), "SMOKE_CREDENTIALS_ALREADY_INSTALLED")
    member = identifier(read("Dedicated member ID (hidden): ").strip())
    email = read("Dedicated member email (hidden): ").strip()
    password = read("Password from Bitwarden (hidden): ")
    confirmation = read("Confirm password (hidden): ")
    ensure(3 <= len(email) <= 254 and 12 <= len(password) <= 128 and password == confirmation, "SMOKE_CREDENTIALS_INVALID")
    account = {"memberId": member, "email": email, "password": password}
    client = client_factory()
    try:
        FunctionalSmoke(client, account, state / "smoke-journal.json").login()
    finally:
        try:
            if client.token is not None:
                client.json("DELETE", "/api/v1/auth/sessions/current", expected=204)
        finally:
            client.clear()
    # Unattended login requires this dedicated credential: private root-only storage is intentional.
    atomic_write(target, json.dumps(account).encode())
    return {"credentialsInstalled": True, "accountCreated": False, "mailSent": False}
