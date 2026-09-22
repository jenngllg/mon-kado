"""Credential-free human-readable evidence derived from merged measurements."""
from datetime import datetime


def resources(samples):
    measured = [sample for sample in samples if sample.get("phase") == "measure"]
    cpu = None
    if len(measured) >= 2:
        first, last = measured[0], measured[-1]
        elapsed = (datetime.fromisoformat(last["observedAt"]) - datetime.fromisoformat(first["observedAt"])).total_seconds()
        if elapsed > 0:
            cpu = (int(last["cpu"]["usage_usec"]) - int(first["cpu"]["usage_usec"])) / elapsed / 10000
    return {"meanMeasuredCpuPercentOfOneCore": cpu,
            "memoryPeakBytesIncludingSetup": max((sample.get("memoryPeak", 0) for sample in samples), default=None),
            "resourceSampleCount": len(samples)}


def markdown(report):
    infrastructure = report["infrastructure"]
    metadata = report["metadata"]
    lines = ["# MK-816 local load test", "",
             f"Profile: {report['profile']}. Verdict: **{report['verdict']}**.", "",
             "This is an isolated local result, not a DigitalOcean throughput guarantee.", "",
             f"Revision: `{metadata.get('revision', 'unknown')}`. Run: `{metadata.get('runId', 'unknown')}`.",
             f"Host: {metadata.get('host', 'unknown')}. Worker: Local; external providers disabled.",
             f"Memory limit: {infrastructure.get('memoryMax')} bytes; swap: {infrastructure.get('swapMax')}; CPU quota: {infrastructure.get('cpuMax')}.",
             f"Requests: {report['completedRequests']}/{report['expectedRequests']}; auxiliary requests: {report['auxiliaryRequests']}.",
             f"Unexpected errors: {report['unexpectedErrors']}; 429: {report['rateLimited']}; dropped iterations: {report['droppedIterations']}.",
             f"OOM kills: {infrastructure.get('oom')}; restarts: {infrastructure.get('restarts')}.",
             f"Stop reason: {report['stopReason'] or 'none'}.", "",
             "| Family | Count | p50 ms | p95 ms | p99 ms |", "|---|---:|---:|---:|---:|"]
    lines.extend(f"| {name} | {item['count']} | {item['p50']} | {item['p95']} | {item['p99']} |"
                 for name, item in report["families"].items())
    lines.extend(["", f"Resource summary: `{report['resourceSummary']}`.",
                  f"Data invariants: `{report['dataInvariants']}`.",
                  "Exact container image identifiers, stage counts, supplemental results and resource samples are in report.json."])
    return "\n".join(lines) + "\n"
