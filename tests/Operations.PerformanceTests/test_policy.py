"""Deterministic policy tests; no Docker daemon or network access."""
import json
import math
import unittest

from policy import (FAMILIES, WEIGHTS, PerformanceError, finite, percentile, require,
                    run_id, safe_point, schedule, verdict)


class PolicyTests(unittest.TestCase):
    def test_run_identifiers_are_closed(self):
        for value in ("mk816-012345abcdef",):
            self.assertEqual(value, run_id(value))
        for value in (None, "", "mon-kado", "../mk816-012345abcdef", "mk816-012345abcdeg"):
            with self.subTest(value=value), self.assertRaisesRegex(PerformanceError, "INVALID_RUN_ID"):
                run_id(value)
        require(True, "UNUSED")
        with self.assertRaisesRegex(PerformanceError, "EXPECTED"):
            require(False, "EXPECTED")

    def test_percentiles_merge_samples_not_percentiles(self):
        self.assertEqual(95, percentile(list(range(101)), 95))
        self.assertEqual(9.5, percentile([0, 10], 95))
        self.assertEqual(2, percentile([2], 50))
        for values in ([], [float("nan")], [-1], [True], ["secret"], [math.inf]):
            with self.subTest(values=values), self.assertRaises(PerformanceError):
                percentile(values, 95)
        self.assertTrue(finite(0))
        self.assertFalse(finite(False))

    def test_profiles_have_fixed_bounded_schedules(self):
        self.assertEqual([(10, 30)], schedule("smoke"))
        self.assertEqual([(10, 900)], schedule("nominal"))
        self.assertEqual([(10, 1500)], schedule("exports"))
        self.assertEqual([(10, 180)], schedule("quotas"))
        self.assertEqual([(5, 300), (10, 300), (20, 300), (5, 300)], schedule("stress"))
        with self.assertRaises(PerformanceError):
            schedule("remote")

    def test_metric_projection_drops_sensitive_tags_and_unknown_metrics(self):
        self.assertIsNone(safe_point({"type": "Metric"}))
        self.assertIsNone(safe_point({"type": "Point", "metric": "http_req_duration"}))
        point = {"type": "Point", "metric": "business_ms", "data": {
            "value": 4, "tags": {"family": "lists", "phase": "measure", "url": "secret", "token": "secret"}}}
        safe = safe_point(point)
        self.assertNotIn("secret", json.dumps(safe))
        self.assertEqual("lists", safe["family"])
        self.assertEqual("auxiliary", safe_point({"type": "Point", "metric": "auxiliary_requests", "data": {"value": 1}})["family"])
        for tags in ({"family": "untrusted"}, {"phase": "untrusted"}):
            with self.subTest(tags=tags), self.assertRaises(PerformanceError):
                safe_point({"type": "Point", "metric": "business_ms", "data": {"value": 1, "tags": tags}})
        with self.assertRaises(PerformanceError):
            safe_point({"type": "Point", "metric": "business_ms"})
        with self.assertRaisesRegex(PerformanceError, "MISSING_SAMPLE_TIME"):
            safe_point({"type": "Point", "metric": "business_ok", "data": {"value": 1}})

    @staticmethod
    def points():
        points = []
        for family, weight in zip(FAMILIES, WEIGHTS):
            for _ in range(weight * 30):
                for metric, value in (("business_ms", 25), ("business_ok", 1), ("business_status", 200)):
                    points.append({"metric": metric, "value": value, "family": family, "phase": "measure",
                                   "timestamp": (len(points) // 3) / 10})
        return points

    def test_complete_success_requires_work_and_verified_limits(self):
        health = {"qualified": True, "alive": True, "oom": 0, "restarts": 0}
        result = verdict(self.points(), "smoke", health)
        self.assertEqual("passed", result["verdict"])
        self.assertFalse(result["productionQualification"])
        self.assertEqual(300, result["completedRequests"])
        self.assertEqual(90, result["families"]["lists"]["count"])
        self.assertEqual("incomplete", verdict([], "smoke", health)["verdict"])
        missing_time = [{**point, "timestamp": None} for point in self.points()]
        self.assertEqual("incomplete", verdict(missing_time, "smoke", health)["verdict"])
        self.assertNotIn("update", verdict([], "images", health)["families"])
        self.assertEqual("failed", verdict(self.points(), "smoke", {})["verdict"])
        self.assertEqual("failed", verdict(self.points(), "smoke", {**health, "alive": False})["verdict"])
        for field in ("oom", "restarts"):
            self.assertEqual("failed", verdict(self.points(), "smoke", {**health, field: 1})["verdict"])
        points = self.points()
        points.append({"metric": "dropped_iterations", "value": 1, "phase": "measure", "family": "auxiliary"})
        self.assertEqual("failed", verdict(points, "smoke", health)["verdict"])
        points = self.points()
        for point in points:
            if point["metric"] == "business_status":
                point["value"] = 429
            if point["metric"] == "business_ok":
                point["value"] = 0
        self.assertEqual("failed", verdict(points, "smoke", health)["verdict"])

    def test_latency_failure_is_not_hidden_by_fast_errors_or_warmup(self):
        health = {"qualified": True, "alive": True, "oom": 0, "restarts": 0}
        points = self.points()
        for point in points:
            if point["metric"] == "business_ms":
                point["value"] = 2000
        self.assertEqual("passed", verdict(points, "smoke", health)["verdict"])
        self.assertEqual("failed", verdict(points * 30, "nominal", health)["verdict"])
        self.assertFalse(verdict(points, "stress", health)["latencyTargetsMet"])
        for point in points:
            point["phase"] = "warmup"
        self.assertEqual("incomplete", verdict(points, "smoke", health)["verdict"])

    def test_complete_counts_do_not_hide_a_lower_observed_rate(self):
        # Arrange
        health = {"qualified": True, "alive": True, "oom": 0, "restarts": 0}
        points = [{**point, "timestamp": (index // 3) / 10 * 1.03}
                  for index, point in enumerate(self.points() * 30)]

        # Act
        result = verdict(points, "nominal", health)

        # Assert
        self.assertEqual(9000, result["completedRequests"])
        self.assertEqual(0, result["unexpectedErrors"])
        self.assertTrue(result["latencyTargetsMet"])
        self.assertFalse(result["throughputTargetsMet"])
        self.assertTrue(result["throughputQualificationRequired"])
        self.assertEqual("local-performance", result["qualificationScope"])
        self.assertLess(result["stages"][0]["observedRequestsPerSecond"], 10)
        self.assertEqual("failed", result["verdict"])

    def test_throughput_boundary_allows_only_floating_point_rounding(self):
        for stretch, expected in ((1, True), (1 + 1e-10, True), (1 + 1e-7, False), (.99, True)):
            with self.subTest(stretch=stretch):
                # Arrange
                health = {"qualified": True, "alive": True, "oom": 0, "restarts": 0}
                points = [{**point, "timestamp": (index // 3) / 10 * stretch}
                          for index, point in enumerate(self.points() * 30)]

                # Act
                result = verdict(points, "nominal", health)

                # Assert
                self.assertEqual(expected, result["throughputTargetsMet"])
                self.assertEqual("passed" if expected else "failed", result["verdict"])

    def test_functional_smoke_records_slow_throughput_without_qualifying_performance(self):
        # Arrange
        health = {"qualified": True, "alive": True, "oom": 0, "restarts": 0}
        points = [{**point, "timestamp": point["timestamp"] * 1.1} for point in self.points()]

        # Act
        result = verdict(points, "smoke", health)

        # Assert
        self.assertEqual("passed", result["verdict"])
        self.assertFalse(result["throughputTargetsMet"])
        self.assertFalse(result["throughputQualificationRequired"])
        self.assertFalse(result["productionQualification"])
        self.assertEqual("functional-smoke", result["qualificationScope"])

    def test_zero_span_cannot_qualify_complete_work(self):
        # Arrange
        health = {"qualified": True, "alive": True, "oom": 0, "restarts": 0}
        points = [{**point, "timestamp": 5} for point in self.points()]

        # Act
        result = verdict(points, "smoke", health)

        # Assert
        self.assertFalse(result["throughputTargetsMet"])
        self.assertIsNone(result["stages"][0]["observedRequestsPerSecond"])
        self.assertEqual("incomplete", result["verdict"])

    def test_stress_requires_each_stage_rate_not_just_an_overall_average(self):
        for slow_stage in (None, 1):
            with self.subTest(slow_stage=slow_stage):
                # Arrange
                health = {"qualified": True, "alive": True, "oom": 0, "restarts": 0}
                points = []
                elapsed = 0
                families = [family for family, weight in zip(FAMILIES, WEIGHTS) for _ in range(weight)]
                for stage, (rate, seconds) in enumerate(schedule("stress")):
                    speed = .9 if stage == slow_stage else 2
                    for index in range(rate * seconds):
                        for metric, value in (("business_ms", 25), ("business_ok", 1), ("business_status", 200)):
                            points.append({"metric": metric, "value": value, "family": families[index % 10],
                                           "phase": "measure", "stage": str(stage), "timestamp": elapsed + index / (rate * speed)})
                    elapsed += seconds / speed

                # Act
                result = verdict(points, "stress", health)

                # Assert
                self.assertGreater(result["observedRequestsPerSecond"], 10)
                self.assertEqual(12000, result["completedRequests"])
                self.assertEqual(slow_stage is None, result["throughputTargetsMet"])
                self.assertEqual("passed" if slow_stage is None else "failed", result["verdict"])
