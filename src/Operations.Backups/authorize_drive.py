"""Interactive workstation OAuth handoff; never print credentials or provider output.

Create the destination directory with an operator-only ACL before running on Windows.
This is a workstation provisioning entrypoint, not part of scheduled VPS execution.
"""

import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    """Load an explicitly selected desktop client and authorize only drive.file."""
    try:
        source, destination, executable = map(Path, sys.argv[1:])
        client = json.loads(source.read_text(encoding="utf-8"))["installed"]
        values = [client["client_id"], client["client_secret"]]
        if any(not isinstance(value, str) or not value or "\n" in value or "\r" in value for value in values):
            raise ValueError()
        descriptor = os.open(destination, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            stream.write("[monkado]\ntype = drive\nscope = drive.file\nclient_id = " + values[0]
                         + "\nclient_secret = " + values[1] + "\n")
        print("Opening the Google authorization page in your browser. Select the dedicated backup account.", flush=True)
        result = subprocess.run([str(executable), "--config", str(destination), "config", "reconnect",
                                 "monkado:", "--auto-confirm"], stdin=subprocess.DEVNULL,
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=600, check=False)
        if result.returncode:
            raise ValueError()
        print("Drive authorization completed. Credentials were saved privately, not printed.")
        return 0
    except Exception:
        print("OAuth setup did not complete. No credential details are displayed.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
