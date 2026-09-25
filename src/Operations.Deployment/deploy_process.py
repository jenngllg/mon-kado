"""Bounded external commands with no ambient Docker context and no diagnostic leakage."""

import os
import subprocess
import tempfile

from deploy_policy import DeploymentError, ensure


def command(arguments, timeout=60, maximum=131072):
    """Capture bounded stdout privately; provider text never reaches a log or exception."""
    environment = {"PATH": "/usr/bin:/bin:/usr/sbin:/sbin", "LANG": "C.UTF-8", "HOME": "/root"}
    try:
        with tempfile.TemporaryFile() as output:
            result = subprocess.run(arguments, env=environment, stdout=output, stderr=subprocess.DEVNULL,
                                    stdin=subprocess.DEVNULL, timeout=timeout, check=False)
            ensure(result.returncode == 0, "COMMAND_FAILED")
            output.seek(0)
            value = output.read(maximum + 1)
            ensure(len(value) <= maximum, "COMMAND_OUTPUT_TOO_LARGE")
            return value
    except (OSError, subprocess.SubprocessError):
        raise DeploymentError("COMMAND_FAILED") from None
