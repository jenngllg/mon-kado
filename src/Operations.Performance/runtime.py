"""Native local orchestration and credential-free merged measurement contracts."""
import json
import os
import subprocess
import time
from datetime import datetime, timezone

from native_runtime import NativeBench, command, private_write, protect_directory, keep_awake
from policy import PerformanceError, require, safe_point, verdict
from reporting import markdown, resources

SUPPLEMENT_PROFILES = ("images", "exports", "quotas", "concurrency")

def check_error_window(recent, observed_since, now):
    if observed_since is not None and now - observed_since >= 60 and recent:
        require(sum(value == 0 for _, value in recent) / len(recent) <= .05, "ERROR_WINDOW_EXCEEDED")


def supplement_result(process, result):
    if process is not None and process.poll() is not None and result is None:
        result = json.loads(process.communicate(timeout=5)[0])
    return result


def validate_supplement(process, result):
    if result is not None:
        require(process.returncode == 0 and result.get("passed") is True, "SUPPLEMENT_FAILED")


class Bench(NativeBench):
    def read_available_points(self, positions):
        points = []
        for shard in range(10):
            with (self.directory / f"metrics-{shard}.jsonl").open(encoding="utf-8") as stream:
                stream.seek(positions[shard])
                while True:
                    before = stream.tell()
                    line = stream.readline()
                    if not line.endswith("\n"):
                        stream.seek(before)
                        break
                    point = safe_point(json.loads(line))
                    if point is not None:
                        points.append(point)
                positions[shard] = stream.tell()
        return points


    def sample_health(self, measured, failed_since):
        health = dict(self.inspect_health())
        health["observedAt"] = datetime.now(timezone.utc).isoformat()
        health["phase"] = "measure" if measured else "warmup"
        require(health["qualified"], "EFFECTIVE_LIMITS_LOST")
        require(health["oom"] == 0 and health["restarts"] == 0, "RESOURCE_FAILURE")
        if health["alive"]:
            failed_since = None
        elif failed_since is None:
            failed_since = time.monotonic()
        require(failed_since is None or time.monotonic() - failed_since < 30, "SERVICE_UNAVAILABLE")
        return health, failed_since


    def sanitize_metrics(self, stop_reason):
        points = []
        for shard in range(10):
            path = self.directory / f"metrics-{shard}.jsonl"
            sanitized = []
            for line in path.read_text().splitlines():
                try:
                    point = safe_point(json.loads(line))
                except (ValueError, PerformanceError):
                    stop_reason = stop_reason or "INCOMPLETE_METRICS"
                    continue
                if point is not None:
                    points.append(point)
                    sanitized.append(json.dumps(point))
            path.write_text("\n".join(sanitized), encoding="utf-8")
            os.chmod(path, 0o600)
        return points, stop_reason


    def write_report(self, profile, processes, points, samples, stop_reason, supplemental_result):
        health = self.inspect_health()
        report = verdict(points, profile, health)
        report["generatorExitCodes"] = [process.returncode for process in processes]
        if any(process.returncode != 0 for process in processes):
            report["verdict"] = "failed"
        report["stopReason"] = stop_reason
        report["supplement"] = supplemental_result
        if stop_reason or (profile in SUPPLEMENT_PROFILES and not supplemental_result):
            report["verdict"] = "failed" if stop_reason else "incomplete"
        report["metadata"] = json.loads((self.directory / "metadata.json").read_text())
        report["auxiliaryRequests"] = sum(point["value"] for point in points if point["metric"] == "auxiliary_requests")
        report["harness"] = json.loads((self.directory / "harness.json").read_text())
        try:
            report["dataInvariants"] = self.verify_data()
        except (PerformanceError, ValueError, KeyError):
            report["dataInvariants"] = {"verified": False}
            report["verdict"] = "failed"
        report["resourceSamples"] = samples
        report["resourceSummary"] = resources(samples, health)
        private_write(self.directory / "report.json", json.dumps(report, indent=2))
        private_write(self.directory / "report.md", markdown(report))
        return report


    def run(self, profile):
        self.stage = "measure"
        require(self.inspect_health()["qualified"], "EFFECTIVE_LIMITS_REQUIRED")
        processes = self.generators(profile)
        samples = []
        positions = [0] * 10
        recent = []
        observed_since = None
        measured = False
        supplement = None
        supplemental_result = None
        stop_reason = None
        failed_since = None
        maximum = time.monotonic() + 2400
        try:
            while any(process.poll() is None for process in processes):
                points = self.read_available_points(positions)
                outcomes = [point for point in points if point["metric"] == "business_ok"]
                recent.extend((point["timestamp"], point["value"]) for point in outcomes)
                if observed_since is None and recent:
                    observed_since = recent[0][0]
                now = datetime.now(timezone.utc).timestamp()
                recent = [(moment, value) for moment, value in recent if now - moment <= 60]
                check_error_window(recent, observed_since, now)
                measured = measured or any(point["phase"] == "measure" for point in outcomes)
                if profile in SUPPLEMENT_PROFILES and supplement is None and measured:
                    supplement = self.start_supplement(profile)
                supplemental_result = supplement_result(supplement, supplemental_result)
                validate_supplement(supplement, supplemental_result)
                health, failed_since = self.sample_health(measured, failed_since)
                samples.append(health)
                require(time.monotonic() < maximum, "CAMPAIGN_TIMEOUT")
                time.sleep(5)
            supplemental_result = supplement_result(supplement, supplemental_result)
            validate_supplement(supplement, supplemental_result)
        except (PerformanceError, OSError, ValueError, KeyboardInterrupt) as error:
            stop_reason = str(error) if isinstance(error, PerformanceError) else "CAMPAIGN_INTERRUPTED"
        finally:
            self.stop_processes(processes, supplement)
        points, stop_reason = self.sanitize_metrics(stop_reason)
        return self.write_report(profile, processes, points, samples, stop_reason, supplemental_result)
