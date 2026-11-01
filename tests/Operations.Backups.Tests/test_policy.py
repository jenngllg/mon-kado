"""Pure policy tests: no real clock, provider, Docker or production access."""

import unittest
from datetime import datetime, timedelta, timezone

from monkado_backup.policy import BackupError, TAG, expired_snapshots, image_reference, release_metadata, snapshot_id, status_health, timestamp, missed_capture


class PolicyTests(unittest.TestCase):
    def test_missed_schedule_handles_both_paris_clock_changes(self):
        # Arrange / Act / Assert
        for now, capture_time in (("2026-03-29T01:16:00Z", "2026-03-29T01:00:00Z"),
                                  ("2026-10-25T02:16:00Z", "2026-10-25T02:00:00Z")):
            with self.subTest(now=now):
                self.assertFalse(missed_capture({"lastCapture": capture_time}, timestamp(now)))
                self.assertTrue(missed_capture({"lastCapture": "2026-01-01T00:00:00Z"}, timestamp(now)))
                self.assertTrue(missed_capture({}, timestamp(now)))
        self.assertFalse(missed_capture({"lastCapture": "2026-03-28T02:00:00Z"},
                                        timestamp("2026-03-29T01:10:00Z")))

    def setUp(self):
        self.now = datetime(2026, 9, 21, 12, tzinfo=timezone.utc)
        self.verified = "a" * 64

    def test_timestamp_requires_explicit_timezone(self):
        # Arrange / Act / Assert
        self.assertEqual(self.now, timestamp("2026-09-21T14:00:00+02:00"))
        for value in (None, 1, "invalid", "2026-09-21T12:00:00"):
            with self.subTest(value=value), self.assertRaises(BackupError):
                timestamp(value)

    def test_only_fixed_immutable_images_are_accepted(self):
        # Arrange / Act / Assert
        valid = "ghcr.io/jenngllg/mon-kado-api@sha256:" + "a" * 64
        self.assertEqual(valid, image_reference(valid, "api"))
        for value in (None, "latest", valid + "\n", valid.replace("jenngllg", "attacker")):
            with self.subTest(value=value), self.assertRaises(BackupError):
                image_reference(value, "api")

    def test_release_pointer_is_not_executable(self):
        # Arrange
        valid = "API_IMAGE=ghcr.io/jenngllg/mon-kado-api@sha256:" + "a" * 64
        valid += "\nWORKER_IMAGE=ghcr.io/jenngllg/mon-kado-worker@sha256:" + "b" * 64
        valid += "\nRELEASE_REVISION=" + "c" * 40 + "\n"
        # Act / Assert
        self.assertEqual("c" * 40, release_metadata(valid)["RELEASE_REVISION"])
        for value in (None, "", "whoami", valid + "COMMAND=whoami", valid.replace("c" * 40, "develop")):
            with self.subTest(value=value), self.assertRaises(BackupError):
                release_metadata(value)

    def test_snapshot_must_be_full_hash(self):
        # Arrange / Act / Assert
        self.assertEqual(self.verified, snapshot_id(self.verified))
        for value in (None, "latest", "abcd", self.verified + "\n"):
            with self.subTest(value=value), self.assertRaises(BackupError):
                snapshot_id(value)

    def test_retention_uses_fourteen_days_and_preserves_verified_and_foreign_snapshots(self):
        # Arrange
        def item(identifier, age, tags=None):
            return {"id": identifier * 64, "time": (self.now - age).isoformat(), "tags": [TAG] if tags is None else tags}
        values = [item("a", timedelta(days=20)), item("b", timedelta(days=14)),
                  item("c", timedelta(days=14, seconds=1)), item("d", timedelta(days=20), ["other"]),
                  item("e", timedelta(days=-1))]
        # Act
        expired = expired_snapshots(values, self.verified, self.now)
        # Assert
        self.assertEqual(["c" * 64], expired)
        with self.assertRaises(BackupError):
            expired_snapshots([], self.verified, self.now)

    def test_status_measures_capture_age_not_last_upload(self):
        # Arrange / Act / Assert
        self.assertEqual("degraded", status_health({}, self.now))
        state = {"lastRemoteCapture": (self.now - timedelta(hours=30)).isoformat()}
        self.assertEqual("healthy", status_health(state, self.now))
        self.assertEqual("degraded", status_health(state, self.now + timedelta(seconds=1)))
        self.assertEqual("degraded", status_health({**state, "error": "FAILED"}, self.now))

    def test_error_codes_never_include_exception_details(self):
        # Arrange / Act / Assert
        self.assertEqual("INVALID_ERROR_CODE", str(BackupError("/private/path secret")))
        self.assertEqual("FAILED", str(BackupError("FAILED")))
