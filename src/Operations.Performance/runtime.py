"""Local Docker orchestration. Captured command output is never echoed on failure."""
import base64
import json
import ipaddress
import os
import secrets
import hashlib
import shutil
import subprocess
import csv
import re
import sys
import time
import ctypes
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path

from compose import configuration
from policy import K6_IMAGE, LABEL, PYTHON_IMAGE, PerformanceError, require, run_id, safe_point, verdict
from seed import dataset, png
from reporting import markdown, resources

SUPPLEMENT_PROFILES = ("images", "exports", "quotas", "concurrency")


@contextmanager
def keep_awake():
    """Prevent idle Windows sleep for this process, without changing power settings."""
    windows = sys.platform == "win32"
    if windows:
        require(bool(ctypes.windll.kernel32.SetThreadExecutionState(0x80000001)), "WAKE_LOCK_UNAVAILABLE")
    try:
        yield
    finally:
        if windows:
            ctypes.windll.kernel32.SetThreadExecutionState(0x80000000)


def command(arguments, *, data=None, timeout=180, include_stderr=False):
    try:
        result = subprocess.run(arguments, input=data, capture_output=True, text=True, timeout=timeout, check=False)
    except (OSError, subprocess.TimeoutExpired):
        raise PerformanceError("COMMAND_UNAVAILABLE") from None
    require(result.returncode == 0, "COMMAND_FAILED")
    return (result.stdout + result.stderr if include_stderr else result.stdout).strip()


def private_write(path, content):
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8") as stream:
        os.chmod(path, 0o600)
        stream.write(content)


def protect_directory(path):
    """Use an owner-only Windows ACL; POSIX chmod alone does not protect NTFS files."""
    if sys.platform == "win32":
        identity = next(csv.reader([command(["whoami", "/user", "/fo", "csv", "/nh"])]))[-1]
        require(re.fullmatch(r"S-1-[0-9-]+", identity) is not None, "OWNER_ID_UNAVAILABLE")
        command(["icacls", str(path), "/inheritance:r", "/grant:r", "*" + identity + ":(OI)(CI)F", "*S-1-5-18:(OI)(CI)F"])


def generator_user():
    """Read owner-only fixtures without restoring DAC-bypass container capabilities."""
    return "0" if sys.platform == "win32" else str(os.getuid())


def check_error_window(recent, observed_since, now):
    if observed_since is not None and now - observed_since >= 60 and recent:
        require(sum(value == 0 for _, value in recent) / len(recent) <= .05, "ERROR_WINDOW_EXCEEDED")


def supplement_result(process, result):
    if process is not None and process.poll() is not None and result is None:
        result = json.loads(process.communicate(timeout=5)[0])
    return result


def validate_supplement(process, result):
    if result is not None:
        require(process.returncode == 0 and result.get("passed") is True, "SUPPLEMENT_FAILED")


class Bench:
    def __init__(self, source, identifier):
        self.source = Path(source).resolve()
        self.identifier = run_id(identifier)
        self.directory = self.source / "TestResults" / "performance" / self.identifier
        self.source_code = self.source / "src" / "Operations.Performance"
        self.code = self.directory / "code"
        self.cg_created = False
        self.driver = "cgroupfs"
        self.stage = "preflight"

    def local_only(self):
        require(not os.environ.get("DOCKER_HOST") and not os.environ.get("DOCKER_CONTEXT"), "DOCKER_OVERRIDE_REFUSED")
        endpoint = command(["docker", "context", "inspect", "--format", "{{.Endpoints.docker.Host}}"])
        require(endpoint.startswith("unix:///") or re.fullmatch(r"npipe:/{4}[.?]/pipe/[A-Za-z0-9_.-]+", endpoint), "REMOTE_DOCKER_REFUSED")
        require(command(["docker", "info", "--format", "{{.OSType}}/{{.CgroupVersion}}"]) == "linux/2", "LINUX_CGROUP_V2_REQUIRED")
        self.driver = command(["docker", "info", "--format", "{{.CgroupDriver}}"])
        require(self.driver in ("cgroupfs", "systemd"), "CGROUP_DRIVER_UNSUPPORTED")

    @property
    def parent(self):
        return self.identifier if self.driver == "cgroupfs" else self.identifier.replace("-", "") + ".slice"

    def compose(self, *args, data=None, timeout=180):
        return command(["docker", "compose", "-p", self.identifier, "-f", str(self.directory / "compose.json"), *args],
                       data=data, timeout=timeout)

    def group(self, action):
        options = ["--privileged"] if action != "status" else []
        helper = self.code if self.code.exists() else self.source_code
        result = command(["docker", "run", "--rm", "--network", "none", *options,
                          "--mount", f"type=bind,source={helper},target=/code,readonly",
                          "--mount", "type=bind,source=/sys/fs/cgroup,target=/host-cgroup" + (",readonly" if action == "status" else ""),
                          PYTHON_IMAGE, "python", "/code/cgroup.py", action, self.identifier, self.driver])
        return json.loads(result)

    def sql(self, query):
        self.owned_containers()
        return self.compose("exec", "-T", "postgres", "psql", "-X", "-qAt", "-v", "ON_ERROR_STOP=1",
                            "-U", "fixture", "-d", self.identifier.replace("-", "_"), data=query)

    def owned_containers(self):
        identifiers = self.compose("ps", "-aq").splitlines()
        for identifier in identifiers:
            label = command(["docker", "inspect", "--format", '{{index .Config.Labels "' + LABEL + '"}}', identifier])
            require(label == self.identifier, "OWNERSHIP_MISMATCH")
        return identifiers

    def create(self):
        self.local_only()
        require(not self.directory.exists(), "RUN_ALREADY_EXISTS")
        for kind in ("container", "volume", "network"):
            existing_names = command(["docker", kind, "ls", *( ["-a"] if kind == "container" else []), "-q", "--filter", "name=" + self.identifier])
            require(not existing_names, "DOCKER_RESOURCES_ALREADY_EXIST")
        self.directory.mkdir(mode=0o700, parents=True)
        protect_directory(self.directory)
        self.code.mkdir(mode=0o700)
        for path in self.source_code.iterdir():
            if path.suffix in (".py", ".js"):
                shutil.copyfile(path, self.code / path.name)
        password = secrets.token_hex(32)
        jwt = base64.b64encode(secrets.token_bytes(32)).decode()
        fixture_password = secrets.token_urlsafe(32) + "!Aa1"
        # Inspect only IPAM ranges, never network secrets or unrelated container configuration.
        networks = command(["docker", "network", "ls", "-q"]).splitlines()
        existing = [ipaddress.ip_network(item["Subnet"]) for network in networks
                    for item in (json.loads(command(["docker", "network", "inspect", "--format", "{{json .IPAM.Config}}", network])) or [])
                    if item.get("Subnet")]
        candidates = (ipaddress.ip_network(f"10.240.{number}.0/24") for number in range(256))
        subnet = next((str(candidate) for candidate in candidates
                       if not any(candidate.overlaps(other) for other in existing)), None)
        require(subnet is not None, "NO_PRIVATE_SUBNET")
        self.stage = "build"
        for target in ("api", "worker"):
            command(["docker", "build", "--target", target, "-t", "mon-kado-" + target + ":mk816", str(self.source)], timeout=600)
        api = command(["docker", "image", "inspect", "mon-kado-api:mk816", "--format", "{{.Id}}"])
        worker = command(["docker", "image", "inspect", "mon-kado-worker:mk816", "--format", "{{.Id}}"])
        cfg = configuration(self.identifier, api, worker, password, jwt, subnet, self.parent)
        private_write(self.directory / "compose.json", json.dumps(cfg))
        caddy = (self.source / "deployments" / "caddy" / "Caddyfile").read_text()
        require(caddy.count("{$API_HOST} {") == 1, "CADDY_TEMPLATE_CHANGED")
        caddy = caddy.replace("{$API_HOST} {", "mk816.test {\n    tls internal", 1)
        private_write(self.directory / "Caddyfile", caddy)
        private_write(self.directory / "metadata.json", json.dumps({
            "schemaVersion": 1, "runId": self.identifier, "api": api, "worker": worker, "k6": K6_IMAGE,
            "revision": command(["git", "-C", str(self.source), "rev-parse", "HEAD"]),
            "dirty": bool(command(["git", "-C", str(self.source), "status", "--porcelain"])),
            "host": command(["docker", "info", "--format", "{{.OperatingSystem}};cpus={{.NCPU}};memory={{.MemTotal}}"]),
            "dataset": {"members": 100, "lists": 500, "wishes": 10000, "images": 200},
            "workerEnvironment": "Local", "externalProviders": False}))
        private_write(self.directory / "harness.json", json.dumps({
            path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in self.code.iterdir()}))
        self.cg_created = True
        self.stage = "cgroup"
        if self.driver == "cgroupfs":
            self.group("create")
        self.stage = "postgres"
        self.compose("up", "-d", "--wait", "postgres")
        if self.driver == "systemd":
            self.owned_containers()
            self.group("configure")
        self.stage = "migrations"
        self.compose("run", "--rm", "migrations", timeout=180)
        self.stage = "seed"
        query, accounts = dataset(self.identifier, fixture_password, secrets.token_bytes(16))
        self.sql(query)
        self.stage = "services"
        # Docker copies this private file as the container's root owner. No host
        # group/world permissions or DAC-bypass capabilities are necessary.
        self.compose("create", "caddy")
        caddy = self.compose("ps", "-aq", "caddy")
        command(["docker", "cp", str(self.directory / "Caddyfile"), f"{caddy}:/etc/caddy/Caddyfile"])
        self.compose("up", "-d", "api", "worker", "caddy")
        # Let the application generate its own Data Protection material and TLS CA.
        deadline = time.monotonic() + 120
        while True:
            try:
                self.compose("exec", "-T", "caddy", "wget", "--header", "Host: mk816.test", "-qO", "/dev/null", "http://api:8080/readiness")
                break
            except PerformanceError:
                require(time.monotonic() < deadline, "READINESS_TIMEOUT")
                time.sleep(2)
        caddy = self.compose("ps", "-q", "caddy")
        command(["docker", "cp", f"{caddy}:/data/caddy/pki/authorities/local/root.crt", str(self.directory / "ca.crt")])
        (self.directory / "image.png").write_bytes(png(128, 128))
        (self.directory / "large.png").write_bytes(png(8000, 5000))
        for shard in range(10):
            private_write(self.directory / f"fixtures-{shard}.json",
                          json.dumps({"accounts": accounts[shard * 10:(shard + 1) * 10], "password": fixture_password}))
        return self.group("status")

    def inspect_health(self):
        cg = self.group("status")
        restarts = 0
        alive = True
        images = {}
        for service in ("api", "worker", "postgres", "caddy"):
            identifier = self.compose("ps", "-aq", service)
            require(bool(identifier), "CONTAINER_MISSING")
            state = json.loads(command(["docker", "inspect", "--format",
                                        '{"image":{{json .Image}},"state":{{json .State.Status}},"oom":{{.State.OOMKilled}},"restarts":{{.RestartCount}},"parent":{{json .HostConfig.CgroupParent}},"ports":{{json .HostConfig.PortBindings}}}', identifier]))
            require(state["parent"] == self.parent and not state["ports"], "ISOLATION_MISMATCH")
            restarts += state["restarts"]
            alive = alive and state["state"] == "running"
            cg["oom"] = max(cg["oom"], int(state["oom"]))
            images[service] = state["image"]
        cg["restarts"] = restarts
        cg["alive"] = alive
        cg["images"] = images
        try:
            self.compose("exec", "-T", "-e", "SSL_CERT_FILE=/data/caddy/pki/authorities/local/root.crt", "caddy", "wget", "-T", "3",
                         "-qO", "/dev/null", "https://mk816.test/readiness", timeout=10)
        except PerformanceError:
            cg["alive"] = False
        return cg

    def verify_data(self):
        result = json.loads(self.sql("""SELECT json_build_object(
            'members',(SELECT count(*) FROM public.users),
            'lists',(SELECT count(*) FROM public.wishlists),
            'wishes',(SELECT count(*) FROM public.wishes),
            'images',(SELECT count(*) FROM public.users WHERE profile_image_id IS NOT NULL)
                   + (SELECT count(*) FROM public.wishes WHERE image_id IS NOT NULL),
            'overReserved',(SELECT count(*) FROM (
                SELECT w.id FROM public.wishes w JOIN public.gift_reservations r ON r.wish_id=w.id
                GROUP BY w.id,w.quantity HAVING sum(r.quantity)>w.quantity) violations));"""))
        require(result["members"] == 100 and result["lists"] == 500 and result["wishes"] == 10000
                and result["images"] >= 200 and result["overReserved"] == 0, "DATA_INVARIANT_FAILED")
        return result

    def generators(self, profile):
        processes = []
        for shard in range(10):
            output = self.directory / f"metrics-{shard}.jsonl"
            private_write(output, "")
            args = ["docker", "run", "--name", f"{self.identifier}-k6-{shard}",
                    "--user", generator_user(),
                    "--label", f"{LABEL}={self.identifier}", "--network", f"{self.identifier}_edge",
                    "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
                    "--tmpfs", "/tmp:rw,noexec,nosuid,size=32m", "--memory", "192m", "--memory-swap", "192m",
                    "--mount", f"type=bind,source={self.code},target=/code,readonly",
                    "--mount", f"type=bind,source={self.directory},target=/fixture,readonly",
                    "--mount", f"type=bind,source={output},target=/metrics.jsonl",
                    "-e", "SSL_CERT_FILE=/fixture/ca.crt", "-e", f"SHARD={shard}", "-e", f"PROFILE={profile}",
                    K6_IMAGE, "run", "--quiet", "--no-usage-report", "--out", "json=/metrics.jsonl", "/code/scenarios.js"]
            processes.append(subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        return processes

    def read_available_points(self, positions):
        points = []
        for shard in range(10):
            with (self.directory / f"metrics-{shard}.jsonl").open(encoding="utf-8") as stream:
                stream.seek(positions[shard])
                while True:
                    before = stream.tell()
                    line = stream.readline()
                    if not line.endswith("\n"):
                        stream.seek(before)
                        break
                    point = safe_point(json.loads(line))
                    if point is not None:
                        points.append(point)
                positions[shard] = stream.tell()
        return points

    def start_supplement(self, profile):
        return subprocess.Popen([
            "docker", "run", "--name", f"{self.identifier}-supplement",
            "--user", generator_user(),
            "--label", f"{LABEL}={self.identifier}", "--network", f"{self.identifier}_edge",
            "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
            "--memory", "384m", "--memory-swap", "384m", "-e", "PYTHONDONTWRITEBYTECODE=1",
            "--mount", f"type=bind,source={self.code},target=/code,readonly",
            "--mount", f"type=bind,source={self.directory},target=/fixture,readonly",
            PYTHON_IMAGE, "python", "/code/supplements.py", profile],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)

    def sample_health(self, measured, failed_since):
        health = dict(self.inspect_health())
        health["observedAt"] = datetime.now(timezone.utc).isoformat()
        health["phase"] = "measure" if measured else "warmup"
        require(health["qualified"], "EFFECTIVE_LIMITS_LOST")
        require(health["oom"] == 0 and health["restarts"] == 0, "RESOURCE_FAILURE")
        if health["alive"]:
            failed_since = None
        elif failed_since is None:
            failed_since = time.monotonic()
        require(failed_since is None or time.monotonic() - failed_since < 30, "SERVICE_UNAVAILABLE")
        return health, failed_since

    def stop_processes(self, processes, supplement):
        for shard, process in enumerate(processes):
            if process.poll() is None:
                command(["docker", "stop", "--time", "5", f"{self.identifier}-k6-{shard}"])
                process.wait(timeout=30)
        if supplement is not None and supplement.poll() is None:
            command(["docker", "stop", "--time", "5", f"{self.identifier}-supplement"])
            supplement.wait(timeout=30)

    def sanitize_metrics(self, stop_reason):
        points = []
        for shard in range(10):
            path = self.directory / f"metrics-{shard}.jsonl"
            sanitized = []
            for line in path.read_text().splitlines():
                try:
                    point = safe_point(json.loads(line))
                except (ValueError, PerformanceError):
                    stop_reason = stop_reason or "INCOMPLETE_METRICS"
                    continue
                if point is not None:
                    points.append(point)
                    sanitized.append(json.dumps(point))
            path.write_text("\n".join(sanitized), encoding="utf-8")
            os.chmod(path, 0o600)
        return points, stop_reason

    def write_report(self, profile, processes, points, samples, stop_reason, supplemental_result):
        health = self.inspect_health()
        report = verdict(points, profile, health)
        report["generatorExitCodes"] = [process.returncode for process in processes]
        if any(process.returncode != 0 for process in processes):
            report["verdict"] = "failed"
        report["stopReason"] = stop_reason
        report["supplement"] = supplemental_result
        if stop_reason or (profile in SUPPLEMENT_PROFILES and not supplemental_result):
            report["verdict"] = "failed" if stop_reason else "incomplete"
        report["metadata"] = json.loads((self.directory / "metadata.json").read_text())
        report["auxiliaryRequests"] = sum(point["value"] for point in points if point["metric"] == "auxiliary_requests")
        report["harness"] = json.loads((self.directory / "harness.json").read_text())
        try:
            report["dataInvariants"] = self.verify_data()
        except (PerformanceError, ValueError, KeyError):
            report["dataInvariants"] = {"verified": False}
            report["verdict"] = "failed"
        report["resourceSamples"] = samples
        report["resourceSummary"] = resources(samples)
        private_write(self.directory / "report.json", json.dumps(report, indent=2))
        private_write(self.directory / "report.md", markdown(report))
        return report


    def run(self, profile):
        self.stage = "measure"
        require(self.inspect_health()["qualified"], "EFFECTIVE_LIMITS_REQUIRED")
        processes = self.generators(profile)
        samples = []
        positions = [0] * 10
        recent = []
        observed_since = None
        measured = False
        supplement = None
        supplemental_result = None
        stop_reason = None
        failed_since = None
        maximum = time.monotonic() + 2400
        try:
            while any(process.poll() is None for process in processes):
                points = self.read_available_points(positions)
                outcomes = [point for point in points if point["metric"] == "business_ok"]
                recent.extend((point["timestamp"], point["value"]) for point in outcomes)
                if observed_since is None and recent:
                    observed_since = recent[0][0]
                now = datetime.now(timezone.utc).timestamp()
                recent = [(moment, value) for moment, value in recent if now - moment <= 60]
                check_error_window(recent, observed_since, now)
                measured = measured or any(point["phase"] == "measure" for point in outcomes)
                if profile in SUPPLEMENT_PROFILES and supplement is None and measured:
                    supplement = self.start_supplement(profile)
                supplemental_result = supplement_result(supplement, supplemental_result)
                validate_supplement(supplement, supplemental_result)
                health, failed_since = self.sample_health(measured, failed_since)
                samples.append(health)
                require(time.monotonic() < maximum, "CAMPAIGN_TIMEOUT")
                time.sleep(5)
            supplemental_result = supplement_result(supplement, supplemental_result)
            validate_supplement(supplement, supplemental_result)
        except (PerformanceError, OSError, ValueError, KeyboardInterrupt) as error:
            stop_reason = str(error) if isinstance(error, PerformanceError) else "CAMPAIGN_INTERRUPTED"
        finally:
            self.stop_processes(processes, supplement)
        points, stop_reason = self.sanitize_metrics(stop_reason)
        return self.write_report(profile, processes, points, samples, stop_reason, supplemental_result)

    def failure_diagnostics(self):
        """Preserve only fixed fields and known exception categories, never log text."""
        output = {}
        if not self.cg_created:
            return output
        for service in ("api", "worker", "postgres", "caddy"):
            try:
                identifier = self.compose("ps", "-aq", service)
                state = json.loads(command(["docker", "inspect", "--format",
                    '{"running":{{.State.Running}},"oom":{{.State.OOMKilled}},"exitCode":{{.State.ExitCode}}}', identifier]))
                logs = command(["docker", "logs", "--tail", "100", identifier], include_stderr=True)
                state["categories"] = [name for name in ("OptionsValidationException", "OutOfMemoryException", "UnauthorizedAccessException",
                    "NpgsqlException", "SocketException", "FileNotFoundException", "permission denied",
                    "operation not permitted", "unrecognized directive", "no such file or directory",
                    "/etc/caddy/Caddyfile", "/data/caddy", "/config/caddy", "/tmp", "frontend") if name in logs]
                output[service] = state
            except (PerformanceError, ValueError, OSError):
                output[service] = {"diagnosticsUnavailable": True}
        return output

    def cleanup(self):
        self.local_only()
        for kind in ("container", "volume", "network"):
            args = ["docker", kind, "ls", "-q", "--filter", f"label={LABEL}={self.identifier}"]
            if kind == "container":
                args.insert(3, "-a")
            for identifier in command(args).splitlines():
                template = "{{json .Labels}}" if kind != "container" else "{{json .Config.Labels}}"
                labels = json.loads(command(["docker", kind, "inspect", "--format", template, identifier]))
                require(labels.get(LABEL) == self.identifier, "OWNERSHIP_MISMATCH")
                removal = ["docker", kind, "rm"]
                if kind == "container":
                    removal.append("-f")
                command([*removal, identifier])
        self.group("remove")
