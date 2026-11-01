"""Workstation-only Google SDK authorization with an OS-selected loopback port."""

import json
import logging
import os
from pathlib import Path
import sys
from datetime import timezone

from google_auth_oauthlib.flow import InstalledAppFlow
from google.auth.transport.requests import Request, AuthorizedSession


def main():
    """Persist a private rclone-compatible token without showing OAuth responses."""
    logging.disable(logging.CRITICAL)
    try:
        source, destination = map(Path, sys.argv[1:])
        if destination.exists():
            raise ValueError()
        config = json.loads(source.read_text(encoding="utf-8"))
        client = config["installed"]
        if client["auth_uri"] != "https://accounts.google.com/o/oauth2/auth" or client["token_uri"] != "https://oauth2.googleapis.com/token":
            raise ValueError()
        scope = "https://www.googleapis.com/auth/drive.file"
        flow = InstalledAppFlow.from_client_config(config, [scope], autogenerate_code_verifier=True)
        print("Opening Google in your browser. Authorize MonKado Backups using its dedicated account.", flush=True)
        credentials = flow.run_local_server(
            host="127.0.0.1", port=0, open_browser=True, timeout_seconds=600,
            authorization_prompt_message="", success_message="MonKado Backups : autorisation reçue. Vous pouvez fermer cet onglet.",
            prompt="consent select_account", login_hint="monkado.app@gmail.com")
        if not credentials.refresh_token:
            raise ValueError()
        credentials.refresh(Request())
        with AuthorizedSession(credentials) as session:
            response = session.get("https://www.googleapis.com/drive/v3/about",
                                   params={"fields": "user(emailAddress)"}, timeout=30)
            response.raise_for_status()
            if response.json().get("user", {}).get("emailAddress", "").casefold() != "monkado.app@gmail.com":
                raise ValueError()
        token = {"access_token": credentials.token, "token_type": "Bearer",
                 "refresh_token": credentials.refresh_token,
                 "expiry": credentials.expiry.replace(tzinfo=timezone.utc).isoformat()}
        descriptor = os.open(destination, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            stream.write("[monkado]\ntype = drive\nscope = drive.file\nclient_id = " + client["client_id"]
                         + "\nclient_secret = " + client["client_secret"] + "\ntoken = " + json.dumps(token) + "\n")
        print("Authorization and token refresh succeeded. Credentials were saved privately.")
        return 0
    except Exception:
        print("Authorization did not complete. No credential details are displayed.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
