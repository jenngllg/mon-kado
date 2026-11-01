"""Neither parallel verification lane can authorize publication on its own."""

import os
from pathlib import Path
import subprocess
import unittest


class BackendQualityGateTests(unittest.TestCase):
    def test_gate_requires_both_lanes_and_rejects_unsuccessful_results(self):
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/ci.yml").read_text()
        gate = workflow.split("  quality:\n", 1)[1]
        self.assertIn("    needs: [backend, operations]\n    if: always()\n", gate)
        command = gate.split("        run: ", 1)[1].strip()
        successful = dict(BACKEND_RESULT="success", OPERATIONS_RESULT="success")
        result = subprocess.run(["bash", "-c", command], env=dict(os.environ, **successful), check=False)
        self.assertEqual(0, result.returncode)
        for dependency in successful:
            for status in ("failure", "cancelled", "skipped", ""):
                with self.subTest(dependency=dependency, status=status):
                    result = subprocess.run(["bash", "-c", command], env=dict(os.environ, **(successful | {dependency: status})), check=False)
                    self.assertNotEqual(0, result.returncode)


if __name__ == "__main__":
    unittest.main()
