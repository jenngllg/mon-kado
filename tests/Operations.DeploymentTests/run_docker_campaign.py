"""Host launcher for the explicitly requested, network-isolated deployment campaign."""

import json
import os
from pathlib import Path
import subprocess
import uuid


def main():
    if os.environ.get("MK820_DOCKER_CAMPAIGN") != "1":
        raise SystemExit("Set MK820_DOCKER_CAMPAIGN=1 to explicitly run disposable Docker tests.")
    root = Path(__file__).resolve().parents[2]
    project = "mk820-test-" + uuid.uuid4().hex[:12]
    label = "mk820.campaign=" + project

    def command(*arguments, check=True):
        return subprocess.run(["docker", *arguments], check=check, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120)

    command("volume", "create", "--label", label, project + "-fixture")
    command("network", "create", "--internal", "--label", label, project + "-control")
    try:
        result = subprocess.run(["docker", "run", "--name", project + "-harness", "--label", label,
                                 "--network", project + "-control", "-e", "MK820_RUN_ID=" + project,
                                 "-e", "MK820_DOCKER_CAMPAIGN=1", "-v", str(root) + ":/source:ro",
                                 "-v", project + "-fixture:/fixture", "-v", "/var/run/docker.sock:/var/run/docker.sock",
                                 "monkado-deployment-tests", "python", "tests/Operations.DeploymentTests/docker_campaign.py"],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=900)
        # The campaign never emits response bodies or credentials. Tracebacks are retained locally, not as CI artifacts.
        print(result.stdout.decode(), end="")
        if result.returncode:
            print(result.stderr.decode(), end="")
        report = root / "TestResults" / "mk820-docker-campaign.jsonl"
        report.parent.mkdir(parents=True, exist_ok=True)
        report.write_bytes(result.stdout)
        return result.returncode
    finally:
        # A terminated test may not have reached its own finally block. Exact unique labels are mandatory.
        containers = command("ps", "-aq", "--filter", "label=" + label).stdout.decode().split()
        for container in containers:
            command("rm", "-f", container)
        networks = command("network", "ls", "-q", "--filter", "label=com.docker.compose.project=" + project).stdout.decode().split()
        for network in networks:
            command("network", "rm", network)
        command("network", "rm", project + "-control")
        volumes = command("volume", "ls", "-q", "--filter", "label=com.docker.compose.project=" + project).stdout.decode().split()
        for volume in volumes:
            command("volume", "rm", volume)
        command("volume", "rm", project + "-fixture")
        images = command("image", "ls", "-q", "--filter", "label=" + label).stdout.decode().split()
        for image in set(images):
            command("image", "rm", image)


if __name__ == "__main__":
    raise SystemExit(main())
