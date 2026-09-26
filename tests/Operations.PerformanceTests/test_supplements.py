import contextlib
import io
import json
import runpy
import unittest
import urllib.error
import zipfile
from unittest.mock import Mock, patch

from policy import PerformanceError
from supplements import (Client, NoRedirect, concurrency, export_check, exports, image_check,
                         images, main, parallel, quotas, prepare_images)


def account(index=0):
    return {"memberId": f"member-{index}", "email": "fake@mk816.invalid",
            "lists": [{"id": f"list-{index}", "wishes": [f"wish-{i}" for i in range(20)]}] * 5}


class SupplementsTests(unittest.TestCase):
    def test_preparation_serializes_twenty_verified_images_and_never_retries(self):
        fixture = json.dumps({"accounts": [account(index) for index in range(10)], "password": "synthetic"})
        with patch("supplements.Path.read_text", return_value=fixture) as read:
            with patch("supplements.Client") as client, patch("supplements.image_check") as image:
                self.assertEqual([{"preparedImages": 20}], prepare_images(9))
                self.assertEqual(10, client.call_count)
                self.assertEqual(20, image.call_count)
                self.assertEqual(["gift", "profile"] * 10, [call.args[1] for call in image.call_args_list])
                self.assertTrue(all(call.kwargs == {"list_index": 0} for call in image.call_args_list))
                read.assert_called_once()
            with patch("supplements.Client"), patch("supplements.image_check", side_effect=PerformanceError("IMAGE_UPLOAD_FAILED")) as image:
                with self.assertRaisesRegex(PerformanceError, "IMAGE_UPLOAD_FAILED"):
                    prepare_images(0)
                image.assert_called_once()
        for shard in (-1, 10):
            with self.assertRaisesRegex(PerformanceError, "INVALID_PREPARATION_SHARD"):
                prepare_images(shard)
        with patch("supplements.Path.read_text", return_value='{"accounts":[]}'):
            with self.assertRaisesRegex(PerformanceError, "INVALID_PREPARATION_ACCOUNTS"):
                prepare_images(0)
        with patch("sys.argv", ["supplements", "prepare", "9"]), patch("supplements.Path.read_text", return_value=fixture):
            with patch("supplements.prepare_images", return_value=[{"preparedImages": 20}]) as prepare:
                with contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(0, main())
                prepare.assert_called_once_with(9)

    def client(self):
        replies = [({"token": "csrf"}, {}), ({"accessToken": "synthetic", "expiresIn": 900}, {}), ({"token": "bound"}, {})]
        with patch("supplements.ssl.create_default_context"), patch("supplements.urllib.request.build_opener"):
            with patch.object(Client, "json_request", side_effect=replies):
                return Client(account(), "synthetic-password")

    def test_client_uses_local_verified_tls_and_refreshes_real_session(self):
        client = self.client()
        response = Mock(code=200, headers={})
        response.read.return_value = b'{"ok":true}'
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=None)
        client.opener.open.return_value = response
        content, _ = client.json_request("GET", "/api/v1/wishlists")
        self.assertTrue(content["ok"])
        self.assertEqual("https://mk816.test/api/v1/wishlists", client.opener.open.call_args.args[0].full_url)
        client.expires = 0
        with patch.object(client, "json_request", side_effect=[({"accessToken": "new", "expiresIn": 900}, {}), ({"token": "next"}, {})]):
            self.assertEqual(200, client.request("GET", "/api/v1/wishlists")[0])
        self.assertEqual("new", client.token)
        with self.assertRaisesRegex(PerformanceError, "UNSAFE_PATH"):
            client.request("GET", "https://api.monkado.fr")
        with self.assertRaisesRegex(PerformanceError, "REDIRECT_REFUSED"):
            NoRedirect().redirect_request(None, None, 302, "", {}, "https://external.invalid")

    def test_http_errors_are_values_and_bodies_are_bounded(self):
        client = self.client()
        failure = urllib.error.HTTPError("https://mk816.test", 429, "limited", {}, io.BytesIO(b"{}"))
        client.opener.open.side_effect = failure
        self.assertEqual(429, client.request("GET", "/api/v1/wishlists")[0])
        response = Mock(code=200, headers={})
        response.read.return_value = b"0" * (16 * 1024 * 1024 + 1)
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=None)
        client.opener.open.side_effect = None
        client.opener.open.return_value = response
        client.token = None
        client.csrf = None
        with self.assertRaisesRegex(PerformanceError, "RESPONSE_TOO_LARGE"):
            client.request("GET", "/security/csrf-token")
        with patch.object(client, "request", return_value=(204, b"", {}, 1)):
            self.assertIsNone(client.json_request("DELETE", "/api/v1/example", expected=(204,))[0])
        with patch.object(client, "request", return_value=(500, b"", {}, 1)):
            with self.assertRaisesRegex(PerformanceError, "UNEXPECTED_HTTP_STATUS"):
                client.json_request("GET", "/api/v1/example")

    def test_image_upload_verifies_success_content_and_stability(self):
        client = Mock(account=account())
        client.json_request.return_value = ({}, {"ETag": '"1"'})
        webp = b"RIFF0000WEBPsynthetic"
        uploaded = b'{"imageUrl":"/api/v1/wishes/image?token=synthetic","profileImageUrl":"https://mk816.test/api/v1/members/image?imageId=synthetic"}'
        for kind in ("gift", "profile"):
            client.request.side_effect = [(200, uploaded, {}, 2), (200, webp, {}, 1), (200, webp, {}, 1)]
            self.assertTrue(image_check(client, kind, b"png")["valid"])
        for responses, code in (
            ([(500, b"", {}, 1)], "IMAGE_UPLOAD_FAILED"),
            ([(200, uploaded, {}, 1), (200, b"bad", {}, 1)], "IMAGE_INVALID"),
            ([(200, uploaded, {}, 1), (200, webp, {}, 1), (200, b"different", {}, 1)], "IMAGE_CHANGED"),
            ([(200, b'{"imageUrl":"https://external.invalid/image"}', {}, 1)], "IMAGE_ORIGIN_REFUSED"),
        ):
            client.request.side_effect = responses
            with self.assertRaisesRegex(PerformanceError, code):
                image_check(client, "gift", b"png")
        with patch("supplements.png", return_value=b"fixture"), patch("supplements.image_check", return_value={"valid": True}):
            self.assertEqual(18, len(images([client, client])))
        with patch("supplements.png", return_value=b"fixture"), patch("supplements.image_check", side_effect=PerformanceError("IMAGE_UPLOAD_FAILED")):
            with self.assertRaisesRegex(PerformanceError, "IMAGE_CASE_FAILED_NORMAL_GIFT"):
                images([client, client])
        self.assertEqual([1, 2], parallel([lambda: 1, lambda: 2]))

    def archive(self, member="member-0", image=True, data=True):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            if image:
                archive.writestr("images/gift.webp", "image")
            if data:
                archive.writestr("data.json", json.dumps({"schemaVersion": 1, "account": {"profile": {"id": member, "imagePath": "images/gift.webp"}},
                                                        "wishlists": [{}] * 5, "wishes": [{}] * 100}))
        return buffer.getvalue()

    def export_client(self, content):
        client = Mock(account=account())
        client.json_request.side_effect = [
            ({"id": "export", "status": "queued"}, {}),
            ({"id": "export", "status": "ready", "sizeInBytes": len(content)}, {})]
        client.request.return_value = (200, content, {}, 1)
        return client

    def test_export_waits_for_worker_and_validates_zip_and_ownership(self):
        content = self.archive()
        with patch("supplements.time.sleep"):
            self.assertTrue(export_check(self.export_client(content))["valid"])
            for content, code in ((self.archive(image=False), "EXPORT_IMAGES_MISSING"),
                                  (self.archive(data=False), "EXPORT_DATA_MISSING"),
                                  (self.archive(member="other"), "EXPORT_WRONG_MEMBER")):
                with self.subTest(code=code), self.assertRaisesRegex(PerformanceError, code):
                    export_check(self.export_client(content))
            client = self.export_client(self.archive())
            client.request.return_value = (404, b"", {}, 1)
            with self.assertRaisesRegex(PerformanceError, "EXPORT_LENGTH"):
                export_check(client)
            client = self.export_client(b"")
            client.json_request.side_effect = [({"id": "export", "status": "failed"}, {})]
            with self.assertRaisesRegex(PerformanceError, "EXPORT_FAILED"):
                export_check(client)
            with patch("supplements.time.monotonic", side_effect=[0, 601]):
                with self.assertRaisesRegex(PerformanceError, "EXPORT_TIMEOUT"):
                    export_check(self.export_client(b""))
        with patch("supplements.export_check", return_value={"valid": True}):
            self.assertEqual([1, 2, 2], [value["concurrency"] for value in exports([1, 2, 3])])
        with patch("supplements.zipfile.ZipFile") as archive, patch("supplements.time.sleep"):
            archive.return_value.__enter__.return_value.testzip.return_value = "broken"
            with self.assertRaisesRegex(PerformanceError, "EXPORT_CRC"):
                export_check(self.export_client(self.archive()))

    def test_quota_is_bounded_and_requires_recovery(self):
        client = Mock()
        success = (200, b"", {}, 1)
        limited = (429, b"", {"Retry-After": "1", "Cache-Control": "no-store"}, 1)
        with patch("supplements.time.sleep"):
            client.request.side_effect = [success, limited, success]
            self.assertTrue(quotas(client)[0]["recovered"])
            for replies, code in (([(500, b"", {}, 1)], "QUOTA_UNEXPECTED_STATUS"),
                                  ([(429, b"", {"Retry-After": "1"}, 1)], "QUOTA_CACHE"),
                                  ([limited, limited], "QUOTA_NOT_RECOVERED"),
                                  ([success] * 310, "QUOTA_NOT_ENFORCED")):
                client.request.side_effect = replies
                with self.subTest(code=code), self.assertRaisesRegex(PerformanceError, code):
                    quotas(client)

    def test_concurrency_asserts_persistence_and_exclusive_reservation(self):
        owner = Mock(account=account())
        first = Mock()
        second = Mock()
        def configure():
            owner.json_request.side_effect = [({}, {"ETag": '"1"'}), ({"name": "Concurrent A"}, {}),
                                              ({"id": "link", "shareUrl": "https://mk816.test/#fake"}, {})]
            owner.request.side_effect = [(200,), (412,)]
            first.request.return_value = (201,)
            second.request.return_value = (409,)
        configure()
        self.assertTrue(concurrency([owner, first, second])[0]["valid"])
        configure()
        owner.request.side_effect = [(200,), (200,)]
        with self.assertRaisesRegex(PerformanceError, "ETAG_CONCURRENCY"):
            concurrency([owner, first, second])
        configure()
        owner.json_request.side_effect = [({}, {"ETag": '"1"'}), ({"name": "wrong"}, {})]
        with self.assertRaisesRegex(PerformanceError, "UPDATE_NOT_PERSISTED"):
            concurrency([owner, first, second])
        configure()
        second.request.return_value = (201,)
        with self.assertRaisesRegex(PerformanceError, "RESERVATION_CONCURRENCY"):
            concurrency([owner, first, second])

    def test_main_emits_only_static_results_or_failure_code(self):
        fixture = json.dumps({"accounts": [account()] * 10, "password": "synthetic"})
        for profile in ("images", "exports", "quotas", "concurrency"):
            with patch("sys.argv", ["supplements", profile]), patch("supplements.Path.read_text", return_value=fixture):
                with patch("supplements.Client"), patch("supplements." + profile, return_value=[]):
                    with contextlib.redirect_stdout(io.StringIO()):
                        self.assertEqual(0, main())
        with patch("sys.argv", ["supplements", "invalid"]), patch("supplements.Path.read_text", return_value=fixture):
            with patch("supplements.Client"), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(1, main())
        with patch("sys.argv", ["supplements", "invalid"]), patch("pathlib.Path.read_text", return_value='{"accounts":[],"password":""}'):
            with contextlib.redirect_stdout(io.StringIO()), self.assertRaises(SystemExit) as exited:
                runpy.run_module("supplements", run_name="__main__")
        self.assertEqual(1, exited.exception.code)
        with patch("sys.argv", ["supplements", "images"]), patch("supplements.Path.read_text", return_value=fixture):
            with patch("supplements.Client", side_effect=OSError("not-for-output")), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(1, main())
            self.assertNotIn("not-for-output", output.getvalue())
