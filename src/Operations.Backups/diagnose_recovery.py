"""Read-only production recovery diagnosis with bounded, secret-free output."""

import json
import os

from monkado_backup.operations import Operations


def main():
    """Inspect fixed native service outcomes without issuing any recovery command."""
    if os.geteuid() != 0:
        print("ROOT_REQUIRED")
        return
    operations = Operations()
    for service in ("api", "worker", "caddy", "postgres"):
        current = operations.service(service)
        print(json.dumps({"service": service, "running": current["running"],
                          "oom": current["oom"], "restarts": current["restarts"]}))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print("DIAGNOSTIC_FAILED_WITHOUT_DISCLOSING_PROVIDER_OUTPUT")
