"""Online deployments preserve traffic and reject incompatible changes before effects."""

import copy
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

from deploy_runtime import OnlineRuntime, Runtime, online_compatible
from deploy_policy import DeploymentError
from test_engine import release


class OnlineRuntimeTests(unittest.TestCase):
    def test_existing_http_contract_must_remain_unchanged(self):
        old = {"paths": {"/api/v1/wishlists": {"get": {"responses": {"200": {"type": "array"}}}}},
               "components": {"schemas": {"Wish": {"type": "object"}}}}
        added = copy.deepcopy(old)
        added["paths"]["/api/v1/new-resource"] = {}
        online_compatible(old, added)
        for invalid in ({}, {"paths": {}, "components": old["components"]},
                        old | {"components": {"schemas": {"Wish": {"type": "string"}}}}):
            with self.subTest(invalid=invalid), self.assertRaises(DeploymentError):
                online_compatible(old, invalid)

    def test_schema_changes_are_blocked_before_preflight_or_stopping_services(self):
        runtime = OnlineRuntime(runner=Mock())
        old, new = release("a"), release("b")
        new["migrationCatalog"] = {"migrations": []}
        with patch.object(Runtime, "preflight") as preflight, self.assertRaisesRegex(DeploymentError, "ONLINE_SCHEMA_REVIEW_REQUIRED"):
            runtime.preflight(new, old)
        preflight.assert_not_called()
        runtime.run.assert_not_called()
        with self.assertRaises(DeploymentError):
            runtime.migrate(new)

    def test_ready_candidate_is_routed_before_old_api_stops_and_proxy_never_stops(self):
        with tempfile.TemporaryDirectory() as temporary:
            events = []
            runtime = OnlineRuntime(root=Path(temporary), state=Path(temporary), runner=Mock(side_effect=lambda args, **kw: events.append(args) or b"not-found"))
            wanted = release("b")
            runtime.prepare = Mock(return_value=Path(temporary))
            runtime.container = Mock(return_value={"running": True, "image": "a" * 64})
            runtime.wait_port = lambda port: events.append(["ready", port])
            runtime.route = lambda port: events.append(["route", port])
            runtime.candidate = lambda _: events.append(["candidateReady", 7001])
            with patch("deploy_runtime.native_artifacts.verify_directory"), patch.object(Runtime, "start", side_effect=lambda *_: events.append(["stableStarted"])):
                runtime.start(wanted)
            self.assertLess(events.index(["candidateReady", 7001]), events.index(["route", 7001]))
            self.assertLess(events.index(["route", 7001]), events.index(["systemctl", "stop", "mon-kado-api.service"]))
            self.assertLess(events.index(["stableStarted"]), events.index(["ready", 7000], events.index(["stableStarted"])))
            self.assertNotIn(["systemctl", "stop", "caddy.service"], events)
            self.assertNotIn(["systemctl", "restart", "caddy.service"], events)

    def test_failed_candidate_keeps_old_api_and_proxy_serving(self):
        runtime = OnlineRuntime(runner=Mock(return_value=b"not-found"))
        runtime.prepare = Mock(return_value=Path("verified"))
        runtime.container = Mock(return_value={"running": True, "image": "a" * 64})
        runtime.wait_port = Mock()
        runtime.route = Mock()
        runtime.candidate = Mock(side_effect=DeploymentError("ONLINE_API_NOT_READY"))
        with patch("deploy_runtime.native_artifacts.verify_directory"), self.assertRaises(DeploymentError):
            runtime.start(release("b"))
        runtime.route.assert_called_once_with(7000)
        self.assertFalse(any(call.args[0] == ["systemctl", "stop", "mon-kado-api.service"] for call in runtime.run.call_args_list))


if __name__ == "__main__":
    unittest.main()
