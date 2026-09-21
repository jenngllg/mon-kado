"""Prove Docker's own ignore processing excludes synthetic credential files."""

import io
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import tarfile
import tempfile
import unittest


@unittest.skipUnless(os.environ.get("MONKADO_CADDY_TESTS") == "1", "Explicit disposable Docker opt-in required")
class BuildContextTests(unittest.TestCase):
    def test_build_context_does_not_include_private_files(self):
        # Arrange
        name = "monkado-security-context-" + secrets.token_hex(8)
        root = Path(__file__).resolve().parents[2]
        private_names = [".env", ".env.production", "src/API/.env", "src/API/.local/key.xml", "production.env",
                         "rclone.conf", "secrets.json", "client_secret_fixture.json", "certificate.pfx",
                         "private.pem", "private.key", "TestResults/synthetic-credentials.txt"]
        with tempfile.TemporaryDirectory(prefix="monkado-context-") as temporary:
            context = Path(temporary)
            shutil.copyfile(root / ".dockerignore", context / ".dockerignore")
            (context / "Dockerfile").write_text("FROM scratch\nCOPY . /\n")
            (context / "public.txt").write_text("public-fixture")
            for relative in private_names:
                path = context / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("synthetic-secret-never-real")
            try:
                # Act
                subprocess.run(["docker", "build", "--quiet", "-t", name, str(context)],
                               check=True, capture_output=True, timeout=120)
                subprocess.run(["docker", "create", "--name", name, name, "/not-started"],
                               check=True, capture_output=True, timeout=30)
                archive = subprocess.run(["docker", "export", name], check=True, capture_output=True, timeout=30).stdout
                with tarfile.open(fileobj=io.BytesIO(archive)) as files:
                    names = set(files.getnames())
                # Assert
                self.assertIn("public.txt", names)
                self.assertFalse(set(private_names).intersection(names))
            finally:
                subprocess.run(["docker", "rm", name], capture_output=True, timeout=30)
                subprocess.run(["docker", "image", "rm", name], capture_output=True, timeout=30)


if __name__ == "__main__":
    unittest.main()
