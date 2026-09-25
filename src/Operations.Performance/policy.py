"""Offline load-test contracts. Never accept an operator-provided remote target."""
import math
import re
from datetime import datetime

K6_IMAGE = "grafana/k6:2.3.0@sha256:9c2dee7f8ed74d317e4027c06a10f169b625638189de8d4555d0b3486a5aeb34"
PYTHON_IMAGE = "python:3.13-slim@sha256:8d9d0b8bcf6506481eae4907c18f5e3e7902e629f5f6d684f9e7c32e85e3ddf0"
MEMORY = 768 * 1024 * 1024
FAMILIES = ("lists", "wishes", "shared", "update", "reservation")
WEIGHTS = (3, 2, 3, 1, 1)
PROFILES = ("smoke", "nominal", "images", "exports", "quotas", "concurrency", "stress")
LABEL = "fr.monkado.performance.run"


class PerformanceError(Exception):
    """A static, credential-free error code suitable for terminal output."""


def require(condition, code):
    if not condition:
        raise PerformanceError(code)


def run_id(value):
    require(isinstance(value, str) and re.fullmatch(r"mk816-[a-f0-9]{12}", value), "INVALID_RUN_ID")
    return value


def finite(value):
    return type(value) in (float, int) and math.isfinite(value) and value >= 0


def percentile(values, percent):
    require(bool(values) and all(finite(value) for value in values), "INVALID_SAMPLES")
    ordered = sorted(values)
    rank = (len(ordered) - 1) * percent / 100
    lower = math.floor(rank)
    upper = math.ceil(rank)
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (rank - lower)


def schedule(profile):
    require(profile in PROFILES, "INVALID_PROFILE")
    if profile == "stress":
        return [(5, 300), (10, 300), (20, 300), (5, 300)]
    if profile == "smoke":
        return [(10, 30)]
    if profile == "exports":
        return [(10, 1500)]
    if profile in ("quotas", "concurrency"):
        return [(10, 180)]
    return [(10, 900)]


def safe_point(record):
    """Strip k6 output to a closed, token-free metric vocabulary before persistence."""
    if record.get("type") != "Point":
        return None
    metric = record.get("metric")
    if metric not in ("business_ms", "business_ok", "business_status", "auxiliary_requests", "dropped_iterations"):
        return None
    data = record.get("data", {})
    value = data.get("value")
    tags = data.get("tags", {})
    family = tags.get("family", "auxiliary")
    phase = tags.get("phase", "measure")
    stage = tags.get("stage", "0")
    require(finite(value) and family in (*FAMILIES, "auxiliary", "image", "export", "quota", "conflict"), "INVALID_METRIC")
    require(phase in ("warmup", "measure", "prepare"), "INVALID_PHASE")
    require(stage in ("0", "1", "2", "3"), "INVALID_STAGE")
    timestamp = datetime.fromisoformat(data["time"].replace("Z", "+00:00")).timestamp() if "time" in data else None
    require(metric != "business_ok" or timestamp is not None, "MISSING_SAMPLE_TIME")
    return {"metric": metric, "value": value, "family": family, "phase": phase, "stage": stage, "timestamp": timestamp}


def verdict(points, profile, infrastructure):
    """Evaluate merged observations, never averages of shard percentiles."""
    measured = [point for point in points if point["phase"] == "measure"]
    stages = schedule(profile)
    expected = sum(rate * seconds for rate, seconds in stages)
    outcomes = [point["value"] for point in measured if point["metric"] == "business_ok"]
    times = [point["timestamp"] for point in measured if point["metric"] == "business_ok" and point.get("timestamp") is not None]
    observed_span = max(times) - min(times) if len(times) >= 2 else 0
    observed_rate = (len(times) - 1) / observed_span if observed_span > 0 else None
    statuses = [point["value"] for point in measured if point["metric"] == "business_status"]
    dropped = sum(point["value"] for point in measured if point["metric"] == "dropped_iterations")
    families = family_results(measured, profile, expected)
    errors = sum(value != 1 for value in outcomes)
    complete = (len(outcomes) == expected and len(statuses) == expected and len(times) == expected and observed_span > 0
                and all(value["count"] >= value["expected"] * .99 for value in families.values()))
    latency_ok = all(value["p95"] is not None and value["p95"] < (1000 if family in ("update", "reservation") else 500)
                     for family, value in families.items())
    stable = (infrastructure.get("qualified") is True and infrastructure.get("alive") is True
              and infrastructure.get("oom", 1) == 0 and infrastructure.get("restarts", 1) == 0)
    success = complete and stable and dropped == 0 and errors / max(1, len(outcomes)) < .01 and 429 not in statuses
    if profile not in ("smoke", "stress"):
        success = success and latency_ok
    stage_results = stage_summaries(measured, stages)
    failed_verdict = "failed" if complete else "incomplete"
    return {"schemaVersion": 1, "profile": profile,
            "verdict": "passed" if success else failed_verdict,
            "expectedRequests": expected, "completedRequests": len(outcomes), "unexpectedErrors": errors,
            "observedCompletionSpanSeconds": observed_span, "observedRequestsPerSecond": observed_rate,
            "rateLimited": statuses.count(429), "droppedIterations": dropped,
            "families": families, "infrastructure": infrastructure,
            "stages": stage_results,
            "latencyTargetsMet": latency_ok, "productionQualification": False}


def family_results(measured, profile, expected):
    families = {}
    weights = (4, 3, 3, 0, 0) if profile in ("images", "exports", "quotas", "concurrency") else WEIGHTS
    for family, weight in zip(FAMILIES, weights):
        if weight == 0:
            continue
        values = [point["value"] for point in measured if point["metric"] == "business_ms" and point["family"] == family]
        families[family] = {"count": len(values), "expected": expected * weight // 10,
                            "p50": percentile(values, 50) if values else None,
                            "p95": percentile(values, 95) if values else None,
                            "p99": percentile(values, 99) if values else None}
    return families


def stage_summaries(measured, stages):
    stage_results = []
    for index, (rate, seconds) in enumerate(stages):
        samples = [point for point in measured if point.get("stage", "0") == str(index)]
        done = [point for point in samples if point["metric"] == "business_ok"]
        stage_results.append({"index": index, "seconds": seconds, "requestedPerSecond": rate,
                              "completedRequests": len(done), "deliveredPerScheduledSecond": len(done) / seconds,
                              "unexpectedErrors": sum(point["value"] != 1 for point in done)})
    return stage_results
