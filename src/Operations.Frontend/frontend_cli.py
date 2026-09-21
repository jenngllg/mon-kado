"""Explicit publisher/administrator entrypoints with sanitized, bounded diagnostics."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

import frontend_contract as contract

INSTALLATION = Path("/opt/monkado")
STATE = Path("/var/lib/monkado-frontend")
BACKEND_STATE = Path("/var/lib/monkado-deployment")


def parser():
    """Separate unprivileged CI packaging from root-only production operations."""
    command = argparse.ArgumentParser()
    operations = command.add_subparsers(dest="operation", required=True)
    package = operations.add_parser("package")
    for name in ("dist", "output", "revision", "backend-revision", "configuration-hash"):
        package.add_argument("--" + name, required=True)
    deploy = operations.add_parser("deploy")
    deploy.add_argument("--retry", action="store_true")
    rollback = operations.add_parser("rollback")
    rollback.add_argument("--revision", required=True)
    operations.add_parser("status")
    return command


def main(arguments):
    """Return only release identifiers, safe state or a generic technical error."""
    options = parser().parse_args(arguments)
    try:
        if options.operation == "package":
            contract.package_build(Path(options.dist), Path(options.output), options.revision,
                                   options.backend_revision, options.configuration_hash)
            print('{"state":"packaged"}')
            return 0
        contract.require(os.geteuid() == 0)
        import frontend_runtime as runtime
        if options.operation == "status":
            value = {"revision": runtime.current_revision(STATE / "releases"), "state": "not-installed"}
            if (STATE / "status.json").exists():
                status = json.loads((STATE / "status.json").read_text(encoding="utf-8"))
                value["state"] = status.get("state") if status.get("state") in {"healthy", "failed"} else "unknown"
            value["interrupted"] = (STATE / "transition.json").exists()
            print(json.dumps(value, sort_keys=True))
            return 0
        result = subprocess.run([sys.executable, str(INSTALLATION / "deployments/production/release_manifest.py"), "hash"],
                                check=True, capture_output=True, text=True, timeout=30)
        configuration_hash = result.stdout.strip()
        contract.require(contract.matches(contract.DIGEST, configuration_hash))
        deployment = runtime.Deployment(STATE, BACKEND_STATE, configuration_hash)
        if options.operation == "rollback":
            contract.require(contract.matches(contract.SHA, options.revision))
            manifest = json.loads((STATE / "manifests" / (options.revision + ".json")).read_text(encoding="utf-8"))
            state = deployment.deploy(approved=manifest, retry=True)
        else:
            state = deployment.deploy(retry=options.retry)
        print(json.dumps({"state": state}))
        return 0
    except Exception:
        print('{"error":"FRONTEND_OPERATION_FAILED"}', file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
