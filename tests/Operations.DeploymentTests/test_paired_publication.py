"""Only two stable, checked main publications can request a grouped rollout."""

from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import wait_paired_publication as publication


class PairedPublicationTests(unittest.TestCase):
    def setUp(self):
        self.heads = {"backend": "a" * 40, "frontend": "b" * 40}
        self.published = dict(self.heads)
        self.elapsed = 0
        self.pause = Mock(side_effect=self.advance)

    def advance(self, seconds):
        self.elapsed += seconds

    def read(self, path):
        component = "frontend" if "/mon-kado-front/" in path else "backend"
        if "/actions/workflows/" in path:
            return {"workflow_runs": []}
        if "/git/ref/" in path:
            return {"object": {"sha": self.heads[component]}}
        return {"tag_name": component + "-production", "draft": False, "prerelease": False,
                "body": json.dumps({"revision": self.published[component]})}

    def wait(self, component="backend", read=None, timeout=10):
        with redirect_stdout(io.StringIO()):
            return publication.wait(component, self.heads[component], read=read or self.read,
                                    clock=lambda: self.elapsed, pause=self.pause, timeout=timeout)

    def test_ready_pair_returns_for_either_request_without_waiting(self):
        for component in self.heads:
            with self.subTest(component=component):
                self.assertEqual(self.heads, self.wait(component))
        self.pause.assert_not_called()

    def test_waits_for_unpublished_peer_then_admits_the_exact_pair(self):
        self.published["frontend"] = "c" * 40
        def advance(seconds):
            self.advance(seconds)
            self.published.update(self.heads)
        self.pause.side_effect = advance
        self.assertEqual(self.heads, self.wait())
        self.pause.assert_called_once_with(10)

    def test_failed_or_unpublished_peer_times_out_without_requesting_rollout(self):
        for component in self.heads:
            with self.subTest(component=component):
                self.published = dict(self.heads) | {component: "c" * 40}
                self.elapsed = 0
                with self.assertRaisesRegex(ValueError, "PAIRED_PUBLICATION_TIMEOUT"):
                    self.wait()
                self.assertEqual(10, self.elapsed)

    def test_either_main_changing_during_wait_aborts_the_group(self):
        for component in self.heads:
            with self.subTest(component=component):
                self.setUp()
                self.published["frontend"] = "c" * 40
                def advance(seconds):
                    self.advance(seconds)
                    self.heads[component] = "d" * 40
                self.pause.side_effect = advance
                with self.assertRaisesRegex(ValueError, "MAIN_REVISION_CHANGED"):
                    self.wait()

    def test_unapproved_or_invalid_pointer_never_becomes_ready(self):
        for field, value in (("draft", True), ("prerelease", True), ("tag_name", "arbitrary"), ("body", '{"revision":"invalid"}')):
            with self.subTest(field=field):
                def read(path):
                    result = self.read(path)
                    return result if "/git/ref/" in path else result | {field: value}
                with self.assertRaises(ValueError):
                    self.wait(read=read)
        self.pause.assert_not_called()

    def test_main_changing_just_after_both_pointers_match_still_blocks_rollout(self):
        def read(path):
            value = self.read(path)
            if path.endswith("releases/tags/frontend-production"):
                self.heads["backend"] = "d" * 40
            return value
        with self.assertRaisesRegex(ValueError, "MAIN_REVISION_CHANGED"):
            self.wait(read=read)
        self.pause.assert_not_called()

    def test_full_backend_gate_can_take_longer_than_the_old_ten_minute_wait(self):
        self.published["backend"] = "c" * 40
        def advance(seconds):
            self.elapsed += seconds
            if self.elapsed >= 14 * 60:
                self.published.update(self.heads)
        self.pause.side_effect = advance
        self.assertEqual(self.heads, self.wait(timeout=publication.WAIT_SECONDS))
        self.assertEqual(14 * 60, self.elapsed)

    def test_failed_exact_revision_check_aborts_without_waiting_for_timeout(self):
        self.published["frontend"] = "c" * 40
        for conclusion in publication.FAILED_CONCLUSIONS:
            with self.subTest(conclusion=conclusion):
                def read(path):
                    if "/actions/workflows/" in path:
                        return {"workflow_runs": [{"event": "push", "run_number": 1, "conclusion": conclusion}]}
                    return self.read(path)
                with self.assertRaisesRegex(ValueError, "PAIRED_CHECKS_FAILED"):
                    self.wait(read=read)
        self.pause.assert_not_called()


if __name__ == "__main__":
    unittest.main()
