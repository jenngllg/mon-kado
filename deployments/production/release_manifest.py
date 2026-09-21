"""Validate the public release contract without executing downloaded content."""

import hashlib
import json
import re
import sys
from pathlib import Path

FILES = (
    "compose.yaml",
    "deployments/caddy/Caddyfile",
    "deployments/production/compose.production.yaml",
    "deployments/production/monkado.slice",
    "deployments/production/deploy.sh",
    "deployments/production/release_manifest.py",
)
REPOSITORY = "jenngllg/mon-kado"


def configuration_hash(root):
    """Bind a release to the reviewed deployment files installed on the VPS."""
    digest = hashlib.sha256()
    for name in FILES:
        digest.update(name.encode() + b"\0")
        digest.update((root / name).read_bytes().replace(b"\r\n", b"\n"))
        digest.update(b"\0")
    return digest.hexdigest()


def validate(value, expected_hash):
    """Reject unknown fields, mutable tags, other repositories and unsafe values."""
    fields = {"schemaVersion", "revision", "configurationHash", "apiImage", "workerImage"}
    if not isinstance(value, dict) or set(value) != fields:
        raise ValueError("Unexpected manifest fields")
    if type(value["schemaVersion"]) is not int or value["schemaVersion"] != 1:
        raise ValueError("Unsupported manifest version")
    if not isinstance(value["revision"], str) or not re.fullmatch(r"[0-9a-f]{40}", value["revision"]):
        raise ValueError("Invalid revision")
    if value["configurationHash"] != expected_hash:
        raise ValueError("Deployment configuration changed: install reviewed files before deploying")
    for key, component in (("apiImage", "api"), ("workerImage", "worker")):
        pattern = rf"ghcr\.io/jenngllg/mon-kado-{component}@sha256:[0-9a-f]{{64}}"
        if not isinstance(value[key], str) or not re.fullmatch(pattern, value[key]):
            raise ValueError("Invalid immutable image reference")
    return value


def from_release(release, expected_hash):
    """Read only the explicit production release, never an arbitrary latest release."""
    if not isinstance(release, dict):
        raise ValueError("Invalid release")
    if release.get("tag_name") != "backend-production" or release.get("draft") is not False or release.get("prerelease") is not False:
        raise ValueError("Not a published production release")
    body = release.get("body")
    if not isinstance(body, str) or len(body) > 4096:
        raise ValueError("Invalid release body")
    return validate(json.loads(body), expected_hash)


def main(arguments):
    """Print either the local contract hash or strictly validated dotenv values."""
    root = Path(__file__).resolve().parents[2]
    expected_hash = configuration_hash(root)
    if arguments == ["hash"]:
        print(expected_hash)
    elif len(arguments) == 2 and arguments[0] == "release":
        data = Path(arguments[1]).read_bytes()
        if len(data) > 65536:
            raise ValueError("Release response too large")
        value = from_release(json.loads(data), expected_hash)
        print(f'API_IMAGE={value["apiImage"]}')
        print(f'WORKER_IMAGE={value["workerImage"]}')
        print(f'RELEASE_REVISION={value["revision"]}')
    else:
        raise ValueError("Usage: release_manifest.py hash | release response.json")


if __name__ == "__main__":
    try:
        main(sys.argv[1:])
    except (ValueError, OSError, TypeError):
        print("Invalid release or local configuration; deployment refused.", file=sys.stderr)
        sys.exit(1)
