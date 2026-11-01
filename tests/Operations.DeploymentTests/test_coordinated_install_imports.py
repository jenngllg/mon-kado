"""Load a staged installer beside a historical installed deployment tree."""

import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class StagedInstallerTests(unittest.TestCase):
    def test_staged_installer_keeps_reviewed_modules_after_loading_installed_manifest(self):
        with tempfile.TemporaryDirectory() as temporary:
            installed = Path(temporary)
            manifest = installed / "deployments/production/release_manifest.py"
            manifest.parent.mkdir(parents=True)
            manifest.write_bytes((ROOT / "deployments/production/release_manifest.py").read_bytes())
            modules = installed / "src/Operations.Deployment"
            modules.mkdir(parents=True)
            (modules / "coordinated_cli.py").write_text('raise RuntimeError("HISTORICAL_TOOL_IMPORTED")\n')
            paths = [ROOT / ("src/Operations." + name) for name in ("Deployment", "Native", "Frontend", "Backups")]
            environment = os.environ | {"PYTHONPATH": os.pathsep.join(map(str, paths + [manifest.parent])),
                                       "PYTHONDONTWRITEBYTECODE": "1"}
            result = subprocess.run([sys.executable, "-c",
                "import coordinated_install_cli as cli; "
                "assert cli.UpgradeEngine.__module__ == 'coordinated_install_engine'; "
                "assert cli.HistoricalSmoke.__module__ == 'coordinated_cli'"],
                cwd=installed, env=environment, capture_output=True, timeout=15, check=False)
            self.assertEqual(0, result.returncode, result.stderr.decode())


if __name__ == "__main__":
    unittest.main()
