"""Read-only production recovery diagnosis with bounded, secret-free output."""

import json
import os
import subprocess

from monkado_backup.operations import Operations


def main():
    """Inspect container identities and simulate the failing Compose start only."""
    if os.geteuid() != 0:
        print("ROOT_REQUIRED")
        return
    operations = Operations()
    for service in ("api", "worker", "caddy", "migrations"):
        identifiers = operations.compose("ps", "--all", "-q", service).split()
        print(json.dumps({"service": service, "containerCount": len(identifiers)}))
    arguments = Operations(run=lambda args: args).compose("--dry-run", "start", "api", "worker", "caddy")
    result = subprocess.run(arguments, capture_output=True, timeout=30, check=False,
                            env={"PATH": "/usr/local/bin:/usr/bin:/bin", "HOME": "/root", "LANG": "C.UTF-8"})
    diagnostic = (result.stdout + result.stderr).decode("utf-8", errors="replace").lower()
    print(json.dumps({"dryRunExitCode": result.returncode,
                      "mentionsMigration": "migration" in diagnostic,
                      "mentionsDependency": "depend" in diagnostic,
                      "mentionsMissingContainer": "no container" in diagnostic or "not found" in diagnostic,
                      "mentionsMemory": "memory" in diagnostic or "oom" in diagnostic}))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print("DIAGNOSTIC_FAILED_WITHOUT_DISCLOSING_PROVIDER_OUTPUT")
