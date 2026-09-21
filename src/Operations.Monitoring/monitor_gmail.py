"""Explicit Gmail alert delivery, independent of the application and its PostgreSQL outbox."""

import base64
from email.message import EmailMessage
import json
import re
import ssl
import urllib.parse
import urllib.request

from monitor_policy import require


class NoRedirect(urllib.request.HTTPRedirectHandler):
    """Never forward an OAuth secret or bearer token to a redirect target."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def post(url, payload, headers):
    """Perform one verified TLS POST; no implicit retry or provider response logging."""
    require(url in ("https://oauth2.googleapis.com/token", "https://gmail.googleapis.com/gmail/v1/users/me/messages/send"))
    context = ssl.create_default_context()
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.verify_mode = ssl.CERT_REQUIRED
    context.check_hostname = True
    opener = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPSHandler(context=context))
    request = urllib.request.Request(url, data=payload, headers=headers, method="POST")
    with opener.open(request, timeout=5) as response:
        data = response.read(65537)
    require(len(data) <= 65536)
    return json.loads(data)


def credentials(value):
    """Validate a dedicated private copy, excluding unrelated application and Drive secrets."""
    require(isinstance(value, dict) and set(value) == {"clientId", "clientSecret", "refreshToken", "sender", "recipient"})
    require(all(isinstance(item, str) and 0 < len(item) <= 4096 and not any(ord(char) < 32 for char in item)
                for item in value.values()))
    require(value["sender"] == "monkado.app@gmail.com")
    require(re.fullmatch(r"[^\s<>@]+@[^\s<>@]+\.[^\s<>@]+", value["recipient"]) is not None)
    return value


def send(value, incidents, keys, now, transport=post):
    """Send one technical incident digest; return only after Gmail acknowledges an identifier."""
    value = credentials(value)
    require(0 < len(keys) <= 100)
    require(all(re.fullmatch(r"[A-Za-z][A-Za-z0-9.]{0,99}", key) is not None for key in keys))
    token = transport("https://oauth2.googleapis.com/token", urllib.parse.urlencode({
        "client_id": value["clientId"], "client_secret": value["clientSecret"],
        "refresh_token": value["refreshToken"], "grant_type": "refresh_token",
    }).encode(), {"Content-Type": "application/x-www-form-urlencoded"})
    require(isinstance(token.get("access_token"), str) and 0 < len(token["access_token"]) <= 8192)
    require(all(33 <= ord(character) <= 126 for character in token["access_token"]))
    message = EmailMessage()
    message["Subject"] = "MonKado — incidents et rétablissements"
    message["From"] = value["sender"]
    message["To"] = value["recipient"]
    lines = ["État technique MonKado — " + now.isoformat(), ""]
    for key in keys:
        incident = incidents[key]
        if key == "monitor.test":
            lines.append("Test volontaire du canal d’alerte — " + incident["changedAt"])
            continue
        lines.append(key + (" : incident ouvert" if incident["open"] else " : rétabli") + " — " + incident["changedAt"])
    message.set_content("\n".join(lines) + "\n\nConsulter le statut local sur le VPS. Aucun redémarrage automatique n'a été effectué.\n")
    payload = json.dumps({"raw": base64.urlsafe_b64encode(message.as_bytes()).decode().rstrip("=")}).encode()
    response = transport("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", payload,
                         {"Content-Type": "application/json", "Authorization": "Bearer " + token["access_token"]})
    require(isinstance(response.get("id"), str) and 0 < len(response["id"]) <= 256)
