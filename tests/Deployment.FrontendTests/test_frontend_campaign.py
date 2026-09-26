"""Exercise the actual release engine and TLS probe without publishing ports or contacting production."""

import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Deployment.SecurityTests"))
from test_caddy import docker, ready


@unittest.skipUnless(os.environ.get("MK936_DOCKER_CAMPAIGN") == "1", "Explicit disposable Docker opt-in required")
class FrontendCampaignTests(unittest.TestCase):
    def test_real_https_publication_rollback_and_interruption(self):
        # Arrange
        name = "monkado-mk936-" + secrets.token_hex(8)
        edge, runner, data, certificates = (name + suffix for suffix in ("-edge", "-runner", "-data", "-certificates"))
        root = Path(__file__).resolve().parents[2]
        docker("network", "create", "--internal", "--label", "monkado.test=" + name, name)
        with tempfile.TemporaryDirectory(prefix="monkado-mk936-") as temporary:
            config = Path(temporary) / "Caddyfile"
            config.write_text('''{
    local_certs
    admin off
}
import /etc/caddy/frontend.caddy
api.monkado.fr {
    header Content-Type text/plain
    respond /readiness "Healthy"
}
''')
            try:
                for volume in (data, certificates):
                    docker("volume", "create", "--label", "monkado.test=" + name, volume)
                docker("run", "-d", "--name", edge, "--network", name, "--label", "monkado.test=" + name,
                       "-e", "FRONTEND_HOST=www.monkado.fr", "-e", "FRONTEND_APEX_HOST=monkado.fr",
                       "-e", "FRONTEND_API_ORIGIN=https://api.monkado.fr", "-e", "FRONTEND_ROOT=/fixture/state/releases/current",
                       "--mount", "type=bind,src=" + str(config) + ",dst=/etc/caddy/Caddyfile,readonly",
                       "--mount", "type=bind,src=" + str(root / "deployments/frontend/frontend.caddy") + ",dst=/etc/caddy/frontend.caddy,readonly",
                       "--mount", "type=volume,src=" + data + ",dst=/fixture,readonly",
                       "--mount", "type=volume,src=" + certificates + ",dst=/data", "caddy:2.11.4-alpine")
                ready(edge, "certificate obtained successfully")
                # Act: same network namespace makes the production probe's loopback resolution local to Caddy.
                result = docker("run", "--rm", "--name", runner, "--network", "container:" + edge,
                                "--label", "monkado.test=" + name, "-e", "CURL_CA_BUNDLE=/certificates/caddy/pki/authorities/local/root.crt",
                                "--mount", "type=bind,src=" + str(root) + ",dst=/source,readonly",
                                "--mount", "type=volume,src=" + data + ",dst=/fixture",
                                "--mount", "type=volume,src=" + certificates + ",dst=/certificates,readonly",
                                "monkado-frontend-tests", "python", "tests/Deployment.FrontendTests/campaign_fixture.py")
                # Assert: persist only fixed, non-sensitive evidence, never subprocess response bodies.
                report = json.loads(result)
                self.assertTrue(report["tlsVerified"])
                self.assertTrue(report["explicitRecoveryVerified"])
                self.assertTrue(json.loads(docker("network", "inspect", name))[0]["Internal"])
                self.assertEqual({}, json.loads(docker("inspect", "--format", "{{json .HostConfig.PortBindings}}", edge)))
                output = root / "TestResults/mk936-frontend-campaign.json"
                output.parent.mkdir(exist_ok=True)
                output.write_text(json.dumps(report, sort_keys=True) + "\n")
            except subprocess.CalledProcessError as failure:
                self.fail("Isolated campaign failed: " + failure.stderr[-4000:])
            finally:
                for container in (runner, edge):
                    subprocess.run(["docker", "rm", "-f", "-v", container], capture_output=True, timeout=30)
                for volume in (data, certificates):
                    subprocess.run(["docker", "volume", "rm", volume], capture_output=True, timeout=30)
                docker("network", "rm", name)


if __name__ == "__main__":
    unittest.main()
