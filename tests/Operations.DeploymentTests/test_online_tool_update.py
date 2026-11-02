"""Operational-only tool upgrades leave the already-serving proxy untouched."""

from pathlib import Path
import unittest
from unittest.mock import Mock, patch

from coordinated_install_runtime import OnlineUpgradeRuntime, UpgradeRuntime


class OnlineToolUpdateTests(unittest.TestCase):
    def test_install_when_proxy_configuration_is_unchanged_does_not_reload_or_change_route(self):
        # Arrange
        runtime = object.__new__(OnlineUpgradeRuntime)
        runtime.native = Mock()
        proxy = "deployments/caddy/Caddyfile"
        intent = {"previousFiles": {proxy: "a" * 64}, "manifest": {"files": {proxy: "a" * 64}}}
        # Act
        with patch.object(UpgradeRuntime, "install_backup_code") as install, patch("deploy_runtime.OnlineRuntime") as router:
            runtime.install_backup_code(intent)
        # Assert
        install.assert_called_once_with(intent)
        router.assert_not_called()
        runtime.native.run.assert_not_called()

    def test_install_when_proxy_configuration_changes_routes_before_install_and_reloads_after(self):
        # Arrange
        runtime = object.__new__(OnlineUpgradeRuntime)
        runtime.native = Mock()
        runtime.root, runtime.state, runtime.settings = map(Path, ("/installed", "/state", "/settings"))
        proxy = "deployments/caddy/Caddyfile"
        intent = {"previousFiles": {proxy: "a" * 64}, "manifest": {"files": {proxy: "b" * 64}}}
        events = []
        runtime.native.run.side_effect = lambda *args, **kwargs: events.append("reload")
        # Act
        with patch.object(UpgradeRuntime, "install_backup_code", side_effect=lambda value: events.append("install")), \
             patch("deploy_runtime.OnlineRuntime") as router:
            router.return_value.route.side_effect = lambda port: events.append(("route", port))
            runtime.install_backup_code(intent)
        # Assert
        self.assertEqual([("route", 7000), "install", "reload"], events)
        args = runtime.native.run.call_args.args[0]
        self.assertIn("reload", args)
        self.assertEqual("/installed/deployments/caddy/Caddyfile", args[args.index("--config") + 1])
