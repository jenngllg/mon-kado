"""Native observation contracts use simulated systemd responses and no real services."""

import hashlib
from pathlib import Path
import tempfile
import unittest

from native_services import PROPERTIES, UNITS, executable_digest, observe, unit


def response(**changes):
    values = {"LoadState": "loaded", "ActiveState": "active", "SubState": "running", "Result": "success",
              "NRestarts": "2", "InvocationID": "a" * 32, "MainPID": "123"}
    values.update(changes)
    return "\n".join(name + "=" + values[name] for name in values)


def runner(raw, started="t 1791750000123456\n", calls=None):
    def invoke(arguments):
        if calls is not None:
            calls.append(arguments)
        return started if arguments[0] == "busctl" else raw
    return invoke


class ServiceTests(unittest.TestCase):
    def test_native_observation_is_bounded_and_contains_no_environment_inspection(self):
        calls = []
        result = observe("api", runner(response().encode(), calls=calls),
                         digest=lambda pid: "b" * 64, boot=lambda: "boot-one")
        self.assertEqual(["systemctl", "show", "--no-pager", "--all",
                          "--property=" + ",".join(PROPERTIES), UNITS["api"]], calls[0])
        self.assertTrue(result["running"])
        self.assertFalse(result["oom"])
        self.assertEqual(2, result["restarts"])
        self.assertEqual(123, result["pid"])
        self.assertEqual("b" * 64, result["image"])
        self.assertTrue(result["startedAt"].endswith("+00:00"))
        self.assertEqual("/org/freedesktop/systemd1/unit/mon_2dkado_2dapi_2eservice", calls[1][4])
        restarted = observe("api", runner(response(InvocationID="c" * 32)), digest=lambda _: "b" * 64, boot=lambda: "boot-one")
        self.assertNotEqual(result["id"], restarted["id"])
        self.assertEqual(result["monitorId"], restarted["monitorId"])
        rebooted = observe("api", runner(response()), digest=lambda _: "b" * 64, boot=lambda: "boot-two")
        self.assertNotEqual(result["monitorId"], rebooted["monitorId"])

    def test_stopped_and_oom_services_are_observed_without_hashing_a_missing_process(self):
        for state, result in (("inactive", "success"), ("failed", "oom-kill")):
            with self.subTest(state=state):
                value = observe("worker", runner(response(ActiveState=state, SubState="dead", Result=result,
                                MainPID="0", InvocationID=""), started="t 0\n"),
                                digest=lambda _: self.fail("stopped process was inspected"), boot=lambda: "boot")
                self.assertFalse(value["running"])
                self.assertEqual(result == "oom-kill", value["oom"])
                self.assertIsNone(value["image"])

    def test_malformed_or_unexpected_properties_remain_unknown(self):
        invalid = [response() + "\nEnvironment=secret", response() + "\nMainPID=123", "x" * 8193,
                   response(LoadState="not-found"), response(ActiveState="unknown"), response(SubState="unknown"),
                   response(MainPID="0"), response(MainPID="-1"), response(MainPID="2147483648"),
                   response(NRestarts="-1"), response(InvocationID="private-value"),
                   response().replace("Result=success\n", "")]
        for raw in invalid:
            with self.subTest(raw=raw[:40]), self.assertRaises(ValueError):
                observe("api", runner(raw), digest=lambda _: "b" * 64, boot=lambda: "boot")
        for started in ("secret timestamp", "t 0\n", "s private-content", "t -1\n"):
            with self.subTest(started=started), self.assertRaises(ValueError):
                observe("api", runner(response(), started=started), digest=lambda _: "b" * 64, boot=lambda: "boot")
        with self.assertRaises(ValueError):
            unit("unreviewed.service")

    def test_executable_digest_streams_only_the_selected_regular_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "123").mkdir()
            body = b"native binary bytes" * 10000
            (root / "123/exe").write_bytes(body)
            self.assertEqual(hashlib.sha256(body).hexdigest(), executable_digest(123, root))
            (root / "123/exe").write_bytes(b"")
            with self.assertRaises(ValueError):
                executable_digest(123, root)
            for pid in (0, -1, "123", True):
                with self.subTest(pid=pid), self.assertRaises(ValueError):
                    executable_digest(pid, root)


if __name__ == "__main__":
    unittest.main()
