"""Publish delivered revisions through channel-scoped Discord webhooks."""

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.error
import urllib.request
import uuid
import zipfile


class PublicationError(Exception):
    """A publication needs operator attention; never include a webhook URL."""


def gh(*args, data=None):
    result = subprocess.run(
        ["gh", *args], input=json.dumps(data) if data is not None else None,
        text=True, capture_output=True, check=False,
    )
    if result.returncode:
        raise PublicationError("GitHub operation failed; publication stopped.")
    return json.loads(result.stdout) if result.stdout.strip() else None


def api(path, data=None):
    return gh("api", path, *(["--input", "-"] if data is not None else []), data=data)


def webhook_request(url, payload, files):
    if not re.fullmatch(r"https://discord\.com/api/webhooks/[0-9]+/[A-Za-z0-9_-]+", url):
        raise PublicationError("Invalid channel webhook configuration.")
    boundary = "monkado-" + uuid.uuid4().hex
    body = bytearray()
    parts = [("payload_json", None, "application/json", json.dumps(payload).encode())]
    parts += [(f"files[{i}]", name, "application/octet-stream", content)
              for i, (name, content) in enumerate(files)]
    for field, name, content_type, content in parts:
        body.extend(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{field}\"".encode())
        if name:
            body.extend(f'; filename="{name}"'.encode())
        body.extend(f"\r\nContent-Type: {content_type}\r\n\r\n".encode())
        body.extend(content)
        body.extend(b"\r\n")
    body.extend(f"--{boundary}--\r\n".encode())
    request = urllib.request.Request(url + "?wait=true", data=bytes(body), headers={
        "Content-Type": f"multipart/form-data; boundary={boundary}",
        "User-Agent": "MonKado-Release-Publisher/1.0",
    })
    # An unacknowledged POST may have succeeded. Keep the pending receipt and
    # never automatically retry it, including after a timeout or rate limit.
    try:
        opener = urllib.request.build_opener(NoRedirect())
        with opener.open(request, timeout=30) as response:
            message = json.loads(response.read(1_000_000))
        if not re.fullmatch(r"[0-9]+", str(message.get("id", ""))):
            raise ValueError("Missing message receipt")
        return {"messageId": message["id"], "channelId": message["channel_id"]}
    except (OSError, ValueError, KeyError):
        raise PublicationError("Discord delivery is unconfirmed. Inspect the pending receipt before retrying.") from None


class NoRedirect(urllib.request.HTTPRedirectHandler):
    """Prevent webhook credentials being forwarded to another destination."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def extension_files(root):
    source = root / "extension"
    manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
    version = manifest["version"]
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){1,3}", version):
        raise PublicationError("Invalid extension version.")
    if manifest.get("host_permissions") != ["https://www.monkado.fr/*"] or any(
        item.get("matches") != ["https://www.monkado.fr/*"]
        for item in manifest.get("content_scripts", [])
    ):
        raise PublicationError("The extension must target the public site only.")
    names = ("manifest.json", "popup.html", "popup.css", "popup.js", "suggestionFlow.js",
             "extract.js", "imageSelection.js", "session-bridge.js", "icon.png", "README.md", "INSTALLATION.md")
    guide = (source / "INSTALLATION.md").read_bytes().replace(b"\r\n", b"\n")
    if not re.search(rf"(?<![0-9.]){re.escape(version)}(?![0-9.])", guide.decode("utf-8")):
        raise PublicationError("Installation guide and extension version differ.")
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in names:
            content = (source / name).read_bytes()
            if name != "icon.png":
                content = content.replace(b"\r\n", b"\n")
            entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, content)
    content = stream.getvalue()
    if len(content) + len(guide) > 8_000_000:
        raise PublicationError("Extension attachments exceed the publication limit.")
    return version, [(f"MonKado-extension-{version}.zip", content),
                     (f"MonKado-installation-{version}.md", guide)]


def package_fingerprint(files):
    """Compare source bytes independently of ZIP compression and text EOLs."""
    entries = []
    for name, content in files:
        if name.endswith(".zip"):
            with zipfile.ZipFile(io.BytesIO(content)) as archive:
                for entry in sorted(archive.namelist()):
                    data = archive.read(entry)
                    if not entry.endswith(".png"):
                        data = data.replace(b"\r\n", b"\n")
                    entries.append((name + "/" + entry, hashlib.sha256(data).hexdigest()))
        else:
            entries.append((name, hashlib.sha256(content.replace(b"\r\n", b"\n")).hexdigest()))
    return hashlib.sha256(json.dumps(entries, sort_keys=True).encode()).hexdigest() if files else None


def publish(repo, revision, tag, payload, files, webhook):
    # GitHub release creation reserves this publication before the Discord POST.
    # A failed/uncertain attempt stays pending and cannot silently duplicate it.
    releases = api(f"repos/{repo}/releases?per_page=100")
    digest = hashlib.sha256(b"".join(content for _, content in files)).hexdigest() if files else None
    fingerprint = package_fingerprint(files)
    previous = next((item for item in releases if item["tag_name"] == tag), None)
    if previous is None:
        # Tags older than the first page must also be detected. The CLI returns
        # 404 separately from transport/authentication failures.
        result = subprocess.run(["gh", "api", f"repos/{repo}/releases/tags/{tag}"],
                                capture_output=True, text=True, check=False)
        if result.returncode == 0:
            previous = json.loads(result.stdout)
        elif "HTTP 404" not in result.stderr:
            raise PublicationError("Cannot verify an earlier publication.")
    if previous:
        receipt = json.loads(previous["body"])
        if receipt.get("packageFingerprint", receipt.get("sha256")) != (
            fingerprint if "packageFingerprint" in receipt else digest
        ):
            raise PublicationError("This version was already reserved with different files; bump the extension version.")
        if receipt.get("state") != "delivered":
            raise PublicationError("Publication is pending. Verify Discord before any manual recovery.")
        print(f"Already published: {tag}")
        return
    receipt = {"state": "pending", "revision": revision, "sha256": digest,
               "packageFingerprint": fingerprint}
    release = api(f"repos/{repo}/releases", {
        "tag_name": tag, "target_commitish": revision, "name": f"Discord receipt: {tag}",
        "body": json.dumps(receipt), "prerelease": True, "make_latest": "false",
    })
    delivered = webhook_request(webhook, payload, files)
    receipt.update(state="delivered", **delivered)
    api(f"repos/{repo}/releases/{release['id']}", {"body": json.dumps(receipt)})
    print(f"Published {tag}: message {delivered['messageId']}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("component", choices=("frontend", "backend"))
    parser.add_argument("--revision", required=True)
    parser.add_argument("--source", type=Path, default=Path("."))
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.revision):
        raise PublicationError("A full delivered revision is required.")
    source_revision = subprocess.run(
        ["git", "-C", str(args.source), "rev-parse", "HEAD"],
        capture_output=True, text=True, check=True,
    ).stdout.strip()
    if source_revision != args.revision:
        raise PublicationError("Source checkout does not match the delivered revision.")
    repo = "jenngllg/mon-kado-front" if args.component == "frontend" else "jenngllg/mon-kado"
    notes = (args.source / "docs" / "discord-release-notes.md").read_text(encoding="utf-8").strip()
    if not notes or len(notes) > 1500:
        raise PublicationError("Public release notes must contain 1–1500 characters.")
    label = "Site" if args.component == "frontend" else "Service MonKado"
    payload = {"content": f"🎁 **{label} — mise à jour en ligne**\n\n{notes}\n\n"
               f"[Détails de la version](https://github.com/{repo}/commit/{args.revision})",
               "allowed_mentions": {"parse": []}}
    if args.dry_run:
        print(json.dumps(payload))
    else:
        publish(repo, args.revision, f"discord-{args.component}-{args.revision}", payload, [],
                os.environ["DISCORD_RELEASES_WEBHOOK"].strip())
    if args.component == "frontend":
        version, files = extension_files(args.source)
        payload = {"content": f"🧩 **Extension MonKado — version {version}**\n\n"
                   "Télécharge le ZIP et le guide correspondant ci-dessous. Décompresse le ZIP avant l’installation.\n"
                   "Pour une mise à jour, remplace les fichiers du dossier déjà chargé, recharge l’extension "
                   "puis actualise ton onglet MonKado.\n\n"
                   "Un problème ? <#1566845662425911450> · Une idée ? <#1566847194701500416>",
                   "allowed_mentions": {"parse": []}}
        if args.dry_run:
            print(json.dumps({"version": version, "files": [name for name, _ in files]}))
        else:
            publish(repo, args.revision, f"discord-extension-{version}", payload, files,
                    os.environ["DISCORD_EXTENSION_WEBHOOK"].strip())


if __name__ == "__main__":
    try:
        main()
    except (PublicationError, KeyError) as error:
        print(str(error) if isinstance(error, PublicationError) else "Missing publication configuration.", file=sys.stderr)
        sys.exit(1)
