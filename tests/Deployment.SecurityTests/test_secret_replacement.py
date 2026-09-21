"""Rehearse database credential replacement using disposable, synthetic values."""

import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import unittest

from test_caddy import docker, ready


@unittest.skipUnless(os.environ.get("MONKADO_CADDY_TESTS") == "1", "Explicit disposable Docker opt-in required")
class SecretReplacementTests(unittest.TestCase):
    def test_database_rotation_requires_matching_persisted_role_and_client_secret(self):
        # Arrange: no host port, no production volume, no secret in arguments or diagnostics.
        name = "monkado-security-database-" + secrets.token_hex(8)
        initial = secrets.token_hex(32)
        replacement = secrets.token_hex(32)
        with tempfile.TemporaryDirectory(prefix="monkado-synthetic-credentials-") as temporary:
            environment = Path(temporary) / "fixture.env"
            environment.write_text("POSTGRES_USER=fixture\nPOSTGRES_INITDB_ARGS=--auth-host=scram-sha-256\nPOSTGRES_PASSWORD=" + initial + "\n")
            try:
                docker("run", "-d", "--name", name, "--env-file", str(environment),
                       "postgres:18.6-alpine")
                # The final server starts after the entrypoint's temporary initialization server.
                ready(name, "database system is ready to accept connections", after="PostgreSQL init process complete")
                self.assertEqual(0, self.connect(name, initial))

                # Act: pipe the synthetic SQL privately, never through a shell argument/history.
                result = subprocess.run(
                    ["docker", "exec", "-i", name, "psql", "-U", "fixture", "-d", "fixture", "-v", "ON_ERROR_STOP=1"],
                    input="ALTER ROLE fixture PASSWORD '" + replacement + "';\n",
                    text=True, capture_output=True, timeout=30)

                # Assert: existing database roles, not startup environment, control new connections.
                self.assertEqual(0, result.returncode)
                self.assertNotEqual(0, self.connect(name, initial))
                self.assertEqual(0, self.connect(name, replacement))
                docker("restart", name)
                ready(name, "Skipping initialization")
                # A fresh connection is made after the restart's ready event.
                ready(name, "database system is ready to accept connections")
                self.assertEqual(0, self.connect(name, replacement))
                self.assertNotEqual(0, self.connect(name, initial))
            finally:
                subprocess.run(["docker", "rm", "-f", "-v", name], capture_output=True, timeout=30)

    @staticmethod
    def connect(name, password):
        result = subprocess.run(
            ["docker", "exec", "-i", name, "sh", "-c",
             'read -r PGPASSWORD; export PGPASSWORD; exec psql -h 127.0.0.1 -U fixture -d fixture -v ON_ERROR_STOP=1 -c "SELECT 1"'],
            input=(password + "\n").encode("ascii"), capture_output=True, timeout=30)
        return result.returncode


if __name__ == "__main__":
    unittest.main()
