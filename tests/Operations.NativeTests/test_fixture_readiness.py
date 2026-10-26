"""TLS fixture readiness follows live certificate installation, without retries."""
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Native.OperationsTests"))
from native_fixture import ready


class FixtureReadinessTests(unittest.TestCase):
    def test_ready_when_certificate_is_issued_waits_for_each_live_cache_entry(self):
        # Arrange
        process = SimpleNamespace(stdout=iter([
            '{"msg":"serving initial configuration"}\n',
            '{"msg":"certificate obtained successfully","identifier":"www.localhost"}\n',
            '{"msg":"certificate obtained successfully","identifier":"api.localhost"}\n',
            '{"msg":"added certificate to cache","subjects":["www.localhost"]}\n',
            '{"msg":"added certificate to cache","subjects":["api.localhost"]}\n',
        ]))
        # Act
        ready(process, "serving initial configuration", ["www.localhost", "api.localhost"])
        # Assert: returning on issuance alone leaves cache-installation events unread.
        self.assertIsNone(next(process.stdout, None))

    def test_ready_when_upstream_has_no_certificates_returns_on_its_start_signal(self):
        # Arrange
        process = SimpleNamespace(stdout=iter(["fixture-ready\n", "next-event\n"]))
        # Act
        ready(process, "fixture-ready")
        # Assert
        self.assertEqual("next-event\n", next(process.stdout))
