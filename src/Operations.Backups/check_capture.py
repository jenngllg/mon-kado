"""Read-only inspection of the interrupted capture; no data or secrets are printed."""

import json
import os

from monkado_backup.capture import verify_manifest
from monkado_backup.operations import STATE


if __name__ == "__main__":
    if os.geteuid() != 0:
        raise SystemExit("ROOT_REQUIRED")
    for name in ("capture.new", "pending"):
        path = STATE / name
        valid = False
        try:
            verify_manifest(path)
            valid = True
        except Exception:
            pass
        print(json.dumps({"capture": name, "exists": path.exists(), "manifestValid": valid}))
    print(json.dumps({"maintenancePending": (STATE / "maintenance.json").exists()}))
