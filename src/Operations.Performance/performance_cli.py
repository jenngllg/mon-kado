"""Operator entry point. No remote URL, credentials or production context accepted."""
import argparse
import json
import secrets
from pathlib import Path

from policy import PROFILES, PerformanceError
from runtime import Bench, private_write, keep_awake


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("run", "cleanup"))
    parser.add_argument("--profile", choices=PROFILES, default="smoke")
    parser.add_argument("--run-id")
    args = parser.parse_args()
    identifier = args.run_id or "mk816-" + secrets.token_hex(6)
    bench = Bench(Path(__file__).resolve().parents[2], identifier)
    try:
        if args.action == "cleanup":
            if args.run_id is None:
                raise PerformanceError("EXPLICIT_RUN_REQUIRED")
            bench.cleanup()
            print(json.dumps({"runId": identifier, "cleaned": True}))
            return 0
        print(json.dumps({"runId": identifier, "profile": args.profile}), flush=True)
        with keep_awake():
            bench.create()
            report = bench.run(args.profile)
        print(json.dumps({"runId": identifier, "verdict": report["verdict"]}), flush=True)
        return 0 if report["verdict"] == "passed" else 1
    except (PerformanceError, OSError, ValueError, KeyboardInterrupt) as error:
        code = str(error) if isinstance(error, PerformanceError) else "CAMPAIGN_NOT_QUALIFIED"
        failure = {"schemaVersion": 1, "runId": identifier, "verdict": "incomplete", "stage": bench.stage, "error": code,
                   "diagnostics": bench.failure_diagnostics()}
        print(json.dumps(failure), flush=True)
        if bench.cg_created and not (bench.directory / "report.json").exists():
            private_write(bench.directory / "report.json", json.dumps(failure, indent=2))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
