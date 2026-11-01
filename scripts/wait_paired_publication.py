"""Wait for both pinned main publications before requesting the shared VPS rollout."""

import json
import os
import re
import subprocess
import sys
import time

REPOSITORIES = {"backend": "mon-kado", "frontend": "mon-kado-front"}
WAIT_SECONDS = 2400
POLL_SECONDS = 10
CHECK_WORKFLOWS = {"backend": ("ci.yml", "codeql.yml"), "frontend": ("quality.yml",)}
FAILED_CONCLUSIONS = {"failure", "cancelled", "timed_out", "action_required", "stale", "skipped"}


def api(path):
    """Read bounded public metadata using the workflow's existing GitHub token."""
    result = subprocess.run(["gh", "api", path], capture_output=True, check=True, timeout=45)
    if len(result.stdout) > 131072:
        raise ValueError("PUBLICATION_METADATA_TOO_LARGE")
    return json.loads(result.stdout)


def main_heads(read):
    """Pin the two fixed production branches, never a caller-selected repository."""
    heads = {}
    for component, repository in REPOSITORIES.items():
        value = read(f"repos/jenngllg/{repository}/git/ref/heads/main")["object"]["sha"]
        if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{40}", value):
            raise ValueError("INVALID_MAIN_REVISION")
        heads[component] = value
    return heads


def published_heads(read):
    """A pointer is readiness metadata; the VPS still authenticates full artifacts."""
    heads = {}
    for component, repository in REPOSITORIES.items():
        tag = component + "-production"
        value = read(f"repos/jenngllg/{repository}/releases/tags/{tag}")
        if value.get("tag_name") != tag or value.get("draft") is not False or value.get("prerelease") is not False:
            raise ValueError("PUBLICATION_NOT_APPROVED")
        revision = json.loads(value["body"])["revision"]
        if not isinstance(revision, str) or not re.fullmatch(r"[0-9a-f]{40}", revision):
            raise ValueError("INVALID_PUBLICATION_REVISION")
        heads[component] = revision
    return heads


def require_pending_or_successful_checks(read, heads):
    """Stop waiting immediately when the latest exact-revision quality run fails."""
    for component, workflows in CHECK_WORKFLOWS.items():
        for workflow in workflows:
            path = f"repos/jenngllg/{REPOSITORIES[component]}/actions/workflows/{workflow}/runs?head_sha={heads[component]}&branch=main&per_page=10"
            runs = [run for run in read(path)["workflow_runs"] if run["event"] in ("push", "workflow_dispatch")]
            if runs and max(runs, key=lambda run: run["run_number"])["conclusion"] in FAILED_CONCLUSIONS:
                raise ValueError("PAIRED_CHECKS_FAILED")


def wait(component, revision, read=api, clock=time.monotonic, pause=time.sleep, timeout=WAIT_SECONDS):
    """Fail before SSH if either checked revision changes or its publication times out."""
    if component not in REPOSITORIES or not isinstance(revision, str) or not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("INVALID_DEPLOYMENT_REQUEST")
    heads = main_heads(read)
    if heads[component] != revision:
        raise ValueError("MAIN_REVISION_CHANGED")
    started = clock()
    deadline = started + timeout
    while True:
        if main_heads(read) != heads:
            raise ValueError("MAIN_REVISION_CHANGED")
        if published_heads(read) == heads:
            if main_heads(read) != heads:
                raise ValueError("MAIN_REVISION_CHANGED")
            if clock() > deadline:
                raise ValueError("PAIRED_PUBLICATION_TIMEOUT")
            print("Both pinned main publications are ready for one coordinated VPS rollout.")
            return heads
        remaining = deadline - clock()
        if remaining <= 0:
            raise ValueError("PAIRED_PUBLICATION_TIMEOUT")
        require_pending_or_successful_checks(read, heads)
        print("Waiting for the other checked main publication.", flush=True)
        # Keep normal paired releases responsive and leave room in the free
        # GITHUB_TOKEN API budget when a full quality gate takes much longer.
        interval = POLL_SECONDS if clock() - started < 120 else 30
        pause(min(interval, remaining))


if __name__ == "__main__":
    try:
        if len(sys.argv) != 3 or not os.environ.get("GH_TOKEN"):
            raise ValueError("INVALID_DEPLOYMENT_REQUEST")
        wait(*sys.argv[1:])
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
        print("PAIRED_PUBLICATION_NOT_READY: no VPS rollout was requested.", file=sys.stderr)
        sys.exit(1)
