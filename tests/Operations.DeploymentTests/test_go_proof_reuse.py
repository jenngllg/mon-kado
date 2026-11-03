"""Untrusted, expired or changed-source CI evidence cannot skip native Go verification."""

from contextlib import chdir, redirect_stdout
from datetime import datetime, timedelta, timezone
import io
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import reuse_go_verification as reuse


class GoProofTests(unittest.TestCase):
    def test_recent_qualified_run_is_found_without_server_conclusion_filter_and_keeps_original_timestamp(self):
        with tempfile.TemporaryDirectory() as temporary, chdir(temporary):
            root = Path(temporary)
            (root / ".github/workflows").mkdir(parents=True)
            (root / "src/Tools.GmailOAuthBootstrap").mkdir(parents=True)
            workflow = "\n".join(reuse.TESTS)
            (root / ".github/workflows/ci.yml").write_text(workflow)
            output = root / "output"
            verified = (datetime.now(timezone.utc) - timedelta(minutes=15)).isoformat()
            tree = b"immutable qualified Go blobs\n"
            proof = {"verifiedAt": verified, "runId": "123", "sourceFingerprint": hashlib.sha256(tree).hexdigest()}
            entries = [{"id": 122, "event": "push", "conclusion": None},
                       {"id": 123, "event": "push", "conclusion": "success", "created_at": verified, "head_sha": "a" * 40}]

            def provider(arguments):
                if arguments[:2] == ["gh", "api"]:
                    # Reproduce a lagging conclusion-filtered history while the
                    # recent-run endpoint already exposes the qualified run.
                    return json.dumps({"workflow_runs": [] if "status=success" in arguments[2] else entries}).encode()
                if arguments[:2] == ["git", "show"]:
                    return workflow.encode()
                if arguments[:2] == ["git", "diff"]:
                    return b""
                if arguments[:2] == ["git", "ls-tree"]:
                    return tree
                self.assertEqual(["gh", "run", "download"], arguments[:3])
                directory = Path(arguments[-1])
                (directory / "src/Tools.GmailOAuthBootstrap").mkdir(parents=True)
                for path in ("coverage.out", "src/Tools.GmailOAuthBootstrap/coverage.out"):
                    (directory / path).write_bytes(b"mode: atomic\n")
                (directory / "go-verification.json").write_text(json.dumps(proof))
                return b""

            with patch.dict(os.environ, {"GITHUB_REPOSITORY": "jenngllg/mon-kado", "GITHUB_OUTPUT": str(output)}), patch.object(reuse, "run", side_effect=provider), redirect_stdout(io.StringIO()):
                reuse.main()
            self.assertEqual("reused=true\n", output.read_text())
            self.assertEqual(proof, json.loads((root / "go-verification.json").read_text()))

    def test_fork_and_expired_runs_are_rejected_before_artifact_download(self):
        now = datetime.now(timezone.utc)
        for event, repository, age in (("pull_request", "outside/fork", 0), ("push", "jenngllg/mon-kado", 7)):
            with self.subTest(event=event, age=age), tempfile.TemporaryDirectory() as temporary:
                output = Path(temporary) / "output"
                evidence = {"workflow_runs": [{"event": event, "conclusion": "success", "head_repository": {"full_name": repository},
                            "created_at": (now - timedelta(hours=age)).isoformat(), "head_sha": "a" * 40}]}
                provider = Mock(return_value=json.dumps(evidence).encode())
                with chdir(ROOT), patch.dict(os.environ, {"GITHUB_REPOSITORY": "jenngllg/mon-kado", "GITHUB_OUTPUT": str(output)}), patch.object(reuse, "run", provider), redirect_stdout(io.StringIO()):
                    reuse.main()
                self.assertEqual("reused=false\n", output.read_text())
                self.assertEqual(1, provider.call_count)

    def test_changed_go_inputs_are_rejected_before_artifact_download(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "output"
            evidence = {"workflow_runs": [{"event": "push", "conclusion": "success", "created_at": datetime.now(timezone.utc).isoformat(), "head_sha": "a" * 40}]}
            provider = Mock(side_effect=[json.dumps(evidence).encode(), (ROOT / ".github/workflows/ci.yml").read_bytes(), b"internal/application/service.go\n"])
            with chdir(ROOT), patch.dict(os.environ, {"GITHUB_REPOSITORY": "jenngllg/mon-kado", "GITHUB_OUTPUT": str(output)}), patch.object(reuse, "run", provider), redirect_stdout(io.StringIO()):
                reuse.main()
            self.assertEqual("reused=false\n", output.read_text())
            self.assertEqual(3, provider.call_count)


if __name__ == "__main__":
    unittest.main()
