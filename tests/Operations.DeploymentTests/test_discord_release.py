"""Offline checks for publication receipts, uncertainty and matching attachments."""

import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
import zipfile


spec = importlib.util.spec_from_file_location(
    "discord_release", Path(__file__).resolve().parents[2] / "scripts/discord_release.py")
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class DiscordPublicationTests(unittest.TestCase):
    def test_publish_when_new_reserves_before_post_and_records_receipt(self):
        events = []

        def api(path, data=None):
            events.append((path, data))
            return [] if "?" in path else {"id": 42}

        def send(*args):
            self.assertEqual(events[-1][1]["tag_name"], "discord-backend-revision")
            self.assertEqual(json.loads(events[-1][1]["body"])["state"], "pending")
            return {"messageId": "123", "channelId": "456"}

        with patch.object(publisher, "api", side_effect=api), \
                patch.object(publisher.subprocess, "run", return_value=Mock(returncode=1, stderr="HTTP 404")), \
                patch.object(publisher, "webhook_request", side_effect=send):
            publisher.publish("repo", "a" * 40, "discord-backend-revision", {}, [], "private")
        self.assertEqual(json.loads(events[-1][1]["body"])["state"], "delivered")

    def test_publish_when_already_delivered_does_not_send(self):
        release = {"tag_name": "tag", "body": json.dumps({"state": "delivered", "sha256": None})}
        with patch.object(publisher, "api", return_value=[release]), \
                patch.object(publisher, "webhook_request") as send:
            publisher.publish("repo", "a" * 40, "tag", {}, [], "private")
        send.assert_not_called()

    def test_publish_when_pending_or_package_changed_does_not_send(self):
        for receipt in ({"state": "pending", "sha256": None},
                        {"state": "delivered", "sha256": "different"}):
            with self.subTest(receipt=receipt), \
                    patch.object(publisher, "api", return_value=[{"tag_name": "tag", "body": json.dumps(receipt)}]), \
                    patch.object(publisher, "webhook_request") as send:
                with self.assertRaises(publisher.PublicationError):
                    publisher.publish("repo", "a" * 40, "tag", {}, [], "private")
                send.assert_not_called()

    def test_webhook_when_timeout_is_sanitized_and_never_retried(self):
        opener = Mock()
        opener.open.side_effect = TimeoutError("https://discord.com/api/webhooks/1/secret")
        with patch.object(publisher.urllib.request, "build_opener", return_value=opener):
            with self.assertRaises(publisher.PublicationError) as raised:
                publisher.webhook_request("https://discord.com/api/webhooks/1/secret", {}, [])
        self.assertNotIn("secret", str(raised.exception))
        opener.open.assert_called_once()

    def test_package_when_docs_match_is_deterministic_and_contains_exact_guide(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "extension"
            source.mkdir()
            for name in ("popup.html", "popup.css", "popup.js", "suggestionFlow.js", "extract.js",
                         "imageSelection.js", "session-bridge.js", "icon.png", "README.md"):
                (source / name).write_bytes(name.encode())
            manifest = {"version": "1.2.0", "host_permissions": ["https://www.monkado.fr/*"],
                        "content_scripts": [{"matches": ["https://www.monkado.fr/*"]}]}
            (source / "manifest.json").write_text(json.dumps(manifest))
            (source / "INSTALLATION.md").write_text("Installation 1.2.0")
            version, files = publisher.extension_files(root)
            self.assertEqual(version, "1.2.0")
            self.assertEqual(files, publisher.extension_files(root)[1])
            with zipfile.ZipFile(io.BytesIO(files[0][1])) as archive:
                self.assertEqual(archive.read("INSTALLATION.md"), files[1][1])
                self.assertEqual(json.loads(archive.read("manifest.json"))["version"], version)
            (source / "INSTALLATION.md").write_text("Installation 1.2.01")
            with self.assertRaises(publisher.PublicationError):
                publisher.extension_files(root)
            manifest["host_permissions"] = ["http://localhost/*"]
            (source / "manifest.json").write_text(json.dumps(manifest))
            with self.assertRaises(publisher.PublicationError):
                publisher.extension_files(root)


if __name__ == "__main__":
    unittest.main()
