"""Explicit local credential handoff; never contact Gmail or enable a service."""

import fcntl
import getpass
import json
import os
from pathlib import Path
import re
import stat
import sys

from monitor_gmail import credentials
from monitor_policy import require
from monitor_storage import atomic_json

SOURCE = Path("/etc/monkado/production.env")
DESTINATION = Path("/etc/monkado/monitoring-gmail.json")
FIELDS = {"GMAIL_CLIENT_ID": "clientId", "GMAIL_CLIENT_SECRET": "clientSecret",
          "GMAIL_REFRESH_TOKEN": "refreshToken", "GMAIL_SENDER_ADDRESS": "sender"}


def read_source(path):
    """Read a bounded root-owned regular file without following a final symlink."""
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, "rb") as source:
        metadata = os.fstat(source.fileno())
        require(stat.S_ISREG(metadata.st_mode) and metadata.st_uid == 0 and
                stat.S_IMODE(metadata.st_mode) == 0o600 and metadata.st_size <= 65536)
        data = source.read(65537)
    require(len(data) <= 65536)
    return data.decode("utf-8")


def extract(source, recipient):
    """Copy four literal Gmail settings only, rejecting ambiguous dotenv syntax."""
    result = {}
    for line in source.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        key, separator, value = line.partition("=")
        key = key.strip()
        if key not in FIELDS:
            continue
        require(separator == "=" and FIELDS[key] not in result)
        value = value.strip()
        if value.startswith(("'", '"')):
            require(len(value) >= 2 and value[-1] == value[0])
            value = value[1:-1]
        # OAuth credentials are literal printable tokens, never shell expressions.
        require(re.fullmatch(r"[A-Za-z0-9._~@/+\-=]{1,4096}", value) is not None)
        result[FIELDS[key]] = value
    return credentials(result | {"recipient": recipient})


def provision(arguments, source=SOURCE, destination=DESTINATION, prompt=getpass.getpass):
    """Install or explicitly replace the dedicated copy; validation precedes any write."""
    require(arguments in (["install"], ["replace"]))
    require(os.geteuid() == 0)
    directory = destination.parent.lstat()
    require(stat.S_ISDIR(directory.st_mode) and directory.st_uid == 0 and directory.st_mode & 0o022 == 0)
    descriptor = os.open(destination.parent / "monitoring-provision.lock",
                         os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, "wb") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        require(not destination.is_symlink())
        require(arguments == ["replace"] or not destination.exists())
        first = prompt("Personal alert recipient (hidden): ")
        require(first == prompt("Confirm the same recipient (hidden): "))
        value = extract(read_source(source), first)
        atomic_json(destination, value)
    return {"credentialsInstalled": True, "mailSent": False, "servicesStarted": False}


def main(arguments=None):
    """Suppress potentially sensitive input and filesystem exceptions at the boundary."""
    arguments = sys.argv[1:] if arguments is None else arguments
    try:
        result = provision(arguments)
    except Exception:
        print(json.dumps({"error": "MONITOR_CREDENTIAL_SETUP_FAILED"}))
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
