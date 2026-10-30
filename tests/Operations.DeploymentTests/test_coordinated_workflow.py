"""Assert the protected coordinator publication retains production gates."""

from pathlib import Path
import re
import unittest


class CoordinatedWorkflowTests(unittest.TestCase):
    def test_publication_requires_protected_environment_and_exact_revision_gates(self):
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/publish-coordinated-tools.yml").read_text()
        self.assertIn("environment: production", workflow)
        self.assertIn("required_reviewers", workflow)
        self.assertIn('for workflow in ci.yml codeql.yml', workflow)
        self.assertIn('head_sha=$GITHUB_SHA&branch=develop', workflow)
        self.assertIn('all(.conclusion == "success")', workflow)
        self.assertIn('coordinator-production', workflow)
        self.assertIn('cmp "$RUNNER_TEMP/existing-coordinator/coordinator.tar.gz"', workflow)
        self.assertNotIn("pull_request_target", workflow)
        self.assertNotIn("continue-on-error", workflow)
        for reference in re.findall(r"uses:\s+(\S+)", workflow):
            self.assertRegex(reference, r"@[0-9a-f]{40}$")


if __name__ == "__main__":
    unittest.main()
