import json
import itertools
import tempfile
import unittest
from datetime import datetime, timezone, timedelta
from pathlib import Path
from unittest.mock import Mock, patch

from runtime import Bench
import test_policy


class CampaignTests(unittest.TestCase):
    def test_sixty_second_error_window_aborts_without_waiting_in_test(self):
        with tempfile.TemporaryDirectory() as root:
            points = test_policy.PolicyTests.points()
            for point in points:
                if point["metric"] == "business_ok":
                    point["value"] = 0
            bench = self.prepare(root, points)
            path = bench.directory / "metrics-0.jsonl"
            old = {"type": "Point", "metric": "business_ok", "data": {
                "value": 0, "time": (datetime.now(timezone.utc) - timedelta(seconds=61)).isoformat(),
                "tags": {"family": "lists", "phase": "measure"}}}
            path.write_text(json.dumps(old) + "\n" + path.read_text())
            process = Mock(returncode=0)
            process.poll.return_value = None
            health = {"qualified": True, "oom": 0, "restarts": 0, "alive": True}
            with patch.object(bench, "generators", return_value=[process]), patch.object(bench, "inspect_health", return_value=health):
                with patch("runtime.command"):
                    self.assertEqual("ERROR_WINDOW_EXCEEDED", bench.run("smoke")["stopReason"])

    def test_supplements_start_only_after_measured_traffic_and_require_success(self):
        for pending in (False, True):
            with self.subTest(pending=pending), tempfile.TemporaryDirectory() as root:
                bench = self.prepare(root, test_policy.PolicyTests.points())
                generator = Mock(returncode=0)
                generator.poll.side_effect = [None, 0, 0]
                supplement = Mock(returncode=0)
                supplement.poll.return_value = None if pending else 0
                supplement.communicate.return_value = ('{"passed":true}', None)
                health = {"qualified": True, "oom": 0, "restarts": 0, "alive": True}
                with patch.object(bench, "generators", return_value=[generator]), patch.object(bench, "inspect_health", return_value=health):
                    with patch("runtime.subprocess.Popen", return_value=supplement), patch("runtime.time.sleep"), patch("runtime.command") as command:
                        result = bench.run("images")
                if pending:
                    self.assertIsNone(result["supplement"])
                    command.assert_called_once_with(["docker", "stop", "--time", "5", bench.identifier + "-supplement"])
                else:
                    self.assertTrue(result["supplement"]["passed"])

    def test_unavailability_and_campaign_deadlines_are_bounded(self):
        for unavailable in (True, False):
            with self.subTest(unavailable=unavailable), tempfile.TemporaryDirectory() as root:
                bench = self.prepare(root)
                process = Mock(returncode=0)
                process.poll.return_value = None
                health = {"qualified": True, "oom": 0, "restarts": 0, "alive": not unavailable}
                clock = itertools.count(0, 11 if unavailable else 2500)
                with patch.object(bench, "generators", return_value=[process]), patch.object(bench, "inspect_health", return_value=health):
                    with patch("runtime.time.monotonic", side_effect=lambda: next(clock)), patch("runtime.time.sleep"), patch("runtime.command"):
                        result = bench.run("smoke")
                self.assertEqual("SERVICE_UNAVAILABLE" if unavailable else "CAMPAIGN_TIMEOUT", result["stopReason"])

    def prepare(self, root, points=None):
        bench = Bench(root, "mk816-012345abcdef")
        bench.verify_data = Mock(return_value={"verified": True})
        bench.directory.mkdir(parents=True)
        (bench.directory / "metadata.json").write_text("{}")
        (bench.directory / "harness.json").write_text("{}")
        for shard in range(10):
            (bench.directory / f"metrics-{shard}.jsonl").write_text("")
        records = [{"type": "Point", "metric": point["metric"], "data": {
            "value": point["value"], "time": datetime.now(timezone.utc).isoformat(),
            "tags": {"family": point["family"], "phase": point["phase"]}}} for point in (points or [])]
        # Unknown telemetry is discarded, not copied into a public artifact.
        records.append({"type": "Metric", "secret": "do-not-persist"})
        (bench.directory / "metrics-0.jsonl").write_text("\n".join(json.dumps(item) for item in records) + "\n")
        return bench

    def test_complete_run_reports_merged_metrics_and_sanitizes_raw_output(self):
        with tempfile.TemporaryDirectory() as root:
            bench = self.prepare(root, test_policy.PolicyTests.points())
            process = Mock(returncode=0)
            process.poll.side_effect = [None, 0, 0]
            health = {"qualified": True, "oom": 0, "restarts": 0, "alive": True}
            with patch.object(bench, "generators", return_value=[process]), patch.object(bench, "inspect_health", return_value=health):
                with patch("runtime.time.sleep"):
                    result = bench.run("smoke")
            self.assertEqual("passed", result["verdict"])
            self.assertNotIn("do-not-persist", (bench.directory / "metrics-0.jsonl").read_text())
            self.assertTrue((bench.directory / "report.md").exists())

    def test_oom_stops_generator_without_retry_and_keeps_failed_report(self):
        with tempfile.TemporaryDirectory() as root:
            bench = self.prepare(root)
            process = Mock(returncode=0)
            process.poll.return_value = None
            health = {"qualified": True, "oom": 1, "restarts": 0, "alive": False}
            with patch.object(bench, "generators", return_value=[process]), patch.object(bench, "inspect_health", return_value=health):
                with patch("runtime.command") as execute:
                    result = bench.run("smoke")
            self.assertEqual("RESOURCE_FAILURE", result["stopReason"])
            self.assertEqual("failed", result["verdict"])
            execute.assert_called_once_with(["docker", "stop", "--time", "5", bench.identifier + "-k6-0"])
            process.wait.assert_called_once_with(timeout=30)

    def test_partial_final_json_is_not_read_until_complete(self):
        with tempfile.TemporaryDirectory() as root:
            bench = self.prepare(root)
            process = Mock(returncode=0)
            process.poll.side_effect = [None, 0, 0]
            path = bench.directory / "metrics-0.jsonl"
            path.write_text('{"incomplete":')
            health = {"qualified": True, "oom": 0, "restarts": 0, "alive": True}
            with patch.object(bench, "generators", return_value=[process]), patch.object(bench, "inspect_health", return_value=health):
                with patch("runtime.time.sleep", side_effect=lambda _: path.write_text("")):
                    self.assertEqual("incomplete", bench.run("smoke")["verdict"])

    def test_data_invariant_failure_prevents_success(self):
        from policy import PerformanceError
        with tempfile.TemporaryDirectory() as root:
            bench = self.prepare(root, test_policy.PolicyTests.points())
            bench.verify_data.side_effect = PerformanceError("DATA_INVARIANT_FAILED")
            process = Mock(returncode=0)
            process.poll.return_value = 0
            health = {"qualified": True, "oom": 0, "restarts": 0, "alive": True}
            with patch.object(bench, "generators", return_value=[process]), patch.object(bench, "inspect_health", return_value=health):
                self.assertEqual("failed", bench.run("smoke")["verdict"])

    def test_nonzero_generator_exit_prevents_success_with_fast_complete_samples(self):
        with tempfile.TemporaryDirectory() as root:
            bench = self.prepare(root, test_policy.PolicyTests.points())
            process = Mock(returncode=99)
            process.poll.return_value = 99
            health = {"qualified": True, "oom": 0, "restarts": 0, "alive": True}
            with patch.object(bench, "generators", return_value=[process]), patch.object(bench, "inspect_health", return_value=health):
                report = bench.run("smoke")
            self.assertEqual("failed", report["verdict"])
            self.assertTrue(report["infrastructure"]["qualified"])
            self.assertEqual([99], report["generatorExitCodes"])

    def test_corrupted_final_metrics_are_incomplete_not_silently_accepted(self):
        with tempfile.TemporaryDirectory() as root:
            bench = self.prepare(root)
            (bench.directory / "metrics-0.jsonl").write_text('{"partial":')
            process = Mock(returncode=0)
            process.poll.return_value = 0
            health = {"qualified": True, "oom": 0, "restarts": 0, "alive": True}
            with patch.object(bench, "generators", return_value=[process]), patch.object(bench, "inspect_health", return_value=health):
                self.assertEqual("INCOMPLETE_METRICS", bench.run("smoke")["stopReason"])
