"""Exercise paired frontend publication using real manifests and archive bytes."""

import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import Mock

ROOT = Path(__file__).resolve().parents[2]
for path in ("src/Operations.Deployment", "src/Operations.Frontend"):
    sys.path.insert(0, str(ROOT / path))
import frontend_contract as contract
import frontend_runtime as hosting
from coordinated_frontend import CoordinatedFrontend
from deploy_policy import DeploymentError


class CoordinatedFrontendTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name)
        self.state = root / "frontend"; self.state.mkdir()
        (self.state / "releases").mkdir()
        self.backend = root / "backend"; self.backend.mkdir()
        self.dist = root / "dist"; self.dist.mkdir()
        (self.dist / "index.html").write_text("<h1>Fixture</h1>")
        (self.dist / "assets").mkdir()
        (self.dist / "assets/main-abcdefgh.js").write_text("// fixture")
        self.archives = {}
        self.old = self.package("a", "b")
        self.new = self.package("d", "e")
        self.new["proof"] = {"schemaVersion": 1, "frontendRevision": "d" * 40, "backendRevision": "e" * 40,
                             "configurationHash": "c" * 64, "openapiSha256": "f" * 64}
        self.remote_proof = self.new["proof"].copy()
        self.approved = self.old["frontend"]
        self.set_backend("b")
        self.health = Mock()
        self.publisher = hosting.Deployment(self.state, self.backend, "c" * 64, self.transport, self.health)
        self.publisher.deploy()
        self.approved = self.new["frontend"]
        self.gate = Mock()
        self.paired = CoordinatedFrontend(self.publisher, self.gate)

    def package(self, frontend, backend):
        output = self.state / ("package-" + frontend)
        manifest = contract.package_build(self.dist, output, frontend * 40, backend * 40, "c" * 64)
        self.archives[contract.archive_url(manifest)] = output / "frontend.tar.gz"
        return {"frontend": manifest, "backend": {"revision": backend * 40}}

    def transport(self, url, destination, limit):
        if url == hosting.POINTER:
            destination.write_text(json.dumps({"tag_name": "frontend-production", "draft": False,
                                              "prerelease": False, "body": json.dumps(self.approved)}))
        elif url.endswith("/coordinated-proof.json"):
            destination.write_text(json.dumps(self.remote_proof))
        else:
            shutil.copyfile(self.archives[url], destination)
        self.assertLessEqual(destination.stat().st_size, limit)

    def set_backend(self, revision):
        (self.backend / "current.env").write_text("RELEASE_REVISION=" + revision * 40 + "\n")

    def test_prepare_activate_and_restore_preserve_each_manifest_binding(self):
        self.paired.prepare(self.old, self.new)
        self.assertEqual(hosting.current_revision(self.publisher.releases), "a" * 40)
        self.set_backend("e")
        self.paired.activate(self.old, self.new)
        self.assertEqual(hosting.current_revision(self.publisher.releases), "d" * 40)
        self.assertEqual(self.publisher.manifest("d" * 40, "e" * 40), self.new["frontend"])
        self.set_backend("b")
        self.paired.restore(self.old, self.new)
        self.assertEqual(hosting.current_revision(self.publisher.releases), "a" * 40)
        self.assertEqual(self.publisher.manifest("a" * 40, "b" * 40), self.old["frontend"])
        self.assertEqual(self.publisher.load()["phase"], "rolledBack")
        self.assertTrue(self.publisher.load()["paused"])
        self.assertEqual(self.gate.verify.call_count, 2)

    def test_activate_when_candidate_backend_is_not_active_does_not_switch(self):
        self.paired.prepare(self.old, self.new)
        with self.assertRaises(DeploymentError):
            self.paired.activate(self.old, self.new)
        self.assertEqual(hosting.current_revision(self.publisher.releases), "a" * 40)

    def test_activate_when_approval_changes_does_not_switch(self):
        self.paired.prepare(self.old, self.new)
        self.set_backend("e")
        self.approved = self.package("f", "e")["frontend"]
        with self.assertRaises(DeploymentError):
            self.paired.activate(self.old, self.new)
        self.assertEqual(hosting.current_revision(self.publisher.releases), "a" * 40)

    def test_activate_when_previous_archive_changes_does_not_switch(self):
        self.paired.prepare(self.old, self.new)
        self.set_backend("e")
        (self.publisher.releases / ("a" * 40) / "index.html").write_text("altered")
        with self.assertRaises(DeploymentError):
            self.paired.activate(self.old, self.new)
        self.assertEqual(hosting.current_revision(self.publisher.releases), "a" * 40)

    def test_activate_when_immutable_contract_proof_changes_does_not_switch(self):
        self.paired.prepare(self.old, self.new)
        self.set_backend("e")
        self.remote_proof["openapiSha256"] = "0" * 64
        with self.assertRaisesRegex(DeploymentError, "FRONTEND_PROOF_CHANGED"):
            self.paired.activate(self.old, self.new)
        self.assertEqual(hosting.current_revision(self.publisher.releases), "a" * 40)

    def test_restore_when_backend_is_wrong_does_not_switch(self):
        self.paired.prepare(self.old, self.new)
        self.set_backend("e")
        self.paired.activate(self.old, self.new)
        with self.assertRaises(DeploymentError):
            self.paired.restore(self.old, self.new)
        self.assertEqual(hosting.current_revision(self.publisher.releases), "d" * 40)


if __name__ == "__main__":
    unittest.main()
