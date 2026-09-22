import unittest

from compose import configuration
from policy import LABEL


class ComposeTests(unittest.TestCase):
    def test_only_owned_internal_networks_and_no_host_ports(self):
        identifier = "mk816-012345abcdef"
        cfg = configuration(identifier, "sha256:api", "sha256:worker", "fake", "fake", "10.240.0.0/24")
        self.assertEqual(identifier, cfg["name"])
        for service in cfg["services"].values():
            self.assertNotIn("ports", service)
            self.assertEqual(identifier, service["labels"][LABEL])
            self.assertEqual(identifier, service["cgroup_parent"])
        for network in cfg["networks"].values():
            self.assertTrue(network["internal"])
        self.assertEqual("Production", cfg["services"]["api"]["environment"]["ASPNETCORE_ENVIRONMENT"])
        self.assertEqual("Local", cfg["services"]["worker"]["environment"]["DOTNET_ENVIRONMENT"])
        self.assertEqual("Disabled", cfg["services"]["worker"]["environment"]["AuthenticationEmail__Provider"])
        self.assertNotIn("GeneralRateLimit__PermitLimit", cfg["services"]["api"]["environment"])
        self.assertNotIn("build", cfg["services"]["api"])
