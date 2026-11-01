"""Assert the protected coordinator publication retains production gates."""

from pathlib import Path
import json
import re
import subprocess
import unittest


class CoordinatedWorkflowTests(unittest.TestCase):
    def test_environment_policy_accepts_only_main_without_human_or_time_gates(self):
        root = Path(__file__).resolve().parents[2]
        environment = {"deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True},
                       "protection_rules": [{"type": "branch_policy"}]}
        for filename in ("publish-coordinated-tools.yml", "publish-backend.yml"):
            workflow = (root / ".github/workflows" / filename).read_text()
            expressions = re.findall(r"--jq '([^']+)' \| grep -x true", workflow)
            for extra, expected in ((None, True), ("required_reviewers", False), ("wait_timer", False)):
                value = environment | {"protection_rules": environment["protection_rules"] + ([{"type": extra}] if extra else [])}
                with self.subTest(workflow=filename, extra=extra):
                    result = subprocess.run(["jq", "-er", expressions[0]], input=json.dumps(value), capture_output=True, text=True)
                    self.assertEqual(expected, result.returncode == 0)
            for branch, expected in (("main", True), ("develop", False), ("*", False)):
                with self.subTest(workflow=filename, branch=branch):
                    result = subprocess.run(["jq", "-er", expressions[1]], input=json.dumps({"branch_policies": [{"name": branch, "type": "branch"}]}), capture_output=True, text=True)
                    self.assertEqual(expected, result.returncode == 0)

    def test_publication_requires_protected_environment_and_exact_revision_gates(self):
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/publish-coordinated-tools.yml").read_text()
        self.assertIn("environment: production", workflow)
        self.assertNotIn("required_reviewers", workflow)
        self.assertIn('select(.type != "branch_policy")', workflow)
        self.assertIn('[{"name":"main","type":"branch"}]', workflow)
        self.assertIn('for workflow in ci.yml codeql.yml', workflow)
        self.assertIn('head_sha=$GITHUB_SHA&branch=main', workflow)
        self.assertIn('all(.conclusion == "success")', workflow)
        self.assertIn('coordinator-production', workflow)
        self.assertIn('cmp "$RUNNER_TEMP/existing-coordinator/coordinator.tar.gz"', workflow)
        self.assertNotIn("pull_request_target", workflow)
        self.assertNotIn("continue-on-error", workflow)
        for reference in re.findall(r"uses:\s+(\S+)", workflow):
            self.assertRegex(reference, r"@[0-9a-f]{40}$")


if __name__ == "__main__":
    unittest.main()
