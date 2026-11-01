"""Bounded native service observations without environments, command lines or log bodies."""

from datetime import datetime, timezone
import hashlib
import os
from pathlib import Path
import re
import stat

UNITS = {"api": "mon-kado-api.service", "worker": "mon-kado-worker.service",
         "postgres": "postgresql@18-main.service", "caddy": "caddy.service"}
PROPERTIES = ("LoadState", "ActiveState", "SubState", "Result", "NRestarts", "InvocationID",
              "MainPID")


def require(condition):
    """Reject malformed observations with a fixed non-sensitive diagnostic."""
    if not condition:
        raise ValueError("NATIVE_SERVICE_OBSERVATION_INVALID")


def unit(service):
    """Resolve only the reviewed installation's fixed service identities."""
    require(service in UNITS)
    return UNITS[service]


def boot_identity():
    """Read the public kernel boot identity without inspecting process configuration."""
    value = Path("/proc/sys/kernel/random/boot_id").read_text(encoding="ascii").strip()
    require(re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", value) is not None)
    return value


def executable_digest(pid, proc=Path("/proc")):
    """Hash the running executable through its kernel descriptor, with bounded streaming I/O."""
    require(type(pid) is int and 0 < pid < 2 ** 31)
    digest = hashlib.sha256()
    with (proc / str(pid) / "exe").open("rb") as stream:
        metadata = os.fstat(stream.fileno())
        require(stat.S_ISREG(metadata.st_mode) and 0 < metadata.st_size <= 128 * 1024 ** 2)
        remaining = metadata.st_size
        while remaining:
            body = stream.read(min(65536, remaining))
            require(bool(body))
            digest.update(body)
            remaining -= len(body)
        require(not stream.read(1))
    return digest.hexdigest()


def observe(service, runner, digest=executable_digest, boot=boot_identity):
    """Read whitelisted systemd properties and a public executable digest only."""
    raw = runner(["systemctl", "show", "--no-pager", "--all",
                  "--property=" + ",".join(PROPERTIES), unit(service)])
    if isinstance(raw, bytes):
        raw = raw.decode("ascii")
    require(isinstance(raw, str) and len(raw) <= 8192)
    values = {}
    for line in raw.splitlines():
        name, separator, value = line.partition("=")
        require(separator == "=" and name in PROPERTIES and name not in values)
        values[name] = value
    require(set(values) == set(PROPERTIES) and values["LoadState"] == "loaded")
    require(values["ActiveState"] in ("active", "inactive", "failed", "activating", "deactivating", "reloading"))
    require(values["SubState"] in ("running", "dead", "failed", "auto-restart", "start", "start-pre", "start-post",
                                    "stop", "stop-sigterm", "stop-sigkill", "stop-post", "exited", "reload"))
    require(values["Result"] in ("success", "resources", "timeout", "exit-code", "signal", "core-dump",
                                  "watchdog", "start-limit-hit", "oom-kill", "exec-condition", "protocol"))
    require(re.fullmatch(r"[0-9]{1,10}", values["NRestarts"]) is not None)
    require(re.fullmatch(r"[0-9]{1,10}", values["MainPID"]) is not None)
    running = values["ActiveState"] == "active" and values["SubState"] == "running"
    pid = int(values["MainPID"])
    invocation = values["InvocationID"]
    require(re.fullmatch(r"[0-9a-f]{32}", invocation) is not None or not running and invocation == "")
    require(not running or 0 < pid < 2 ** 31)
    # systemctl show formats timestamps independently of --timestamp; D-Bus preserves exact microseconds.
    escaped = "".join(character if character.isascii() and character.isalnum() else f"_{ord(character):02x}"
                      for character in unit(service))
    start = runner(["busctl", "--system", "get-property", "org.freedesktop.systemd1",
                    "/org/freedesktop/systemd1/unit/" + escaped, "org.freedesktop.systemd1.Service", "ExecMainStartTimestamp"])
    if isinstance(start, bytes):
        start = start.decode("ascii")
    require(isinstance(start, str) and re.fullmatch(r"t [0-9]{1,18}\n?", start) is not None)
    microseconds = int(start.split()[1])
    require(not running or microseconds > 0)
    seconds, fraction = divmod(microseconds, 1000000)
    started = datetime.fromtimestamp(seconds, timezone.utc).replace(microsecond=fraction).isoformat() if microseconds else None
    identity = hashlib.sha256((unit(service) + "\0" + invocation).encode("ascii")).hexdigest()
    monitor_identity = hashlib.sha256((unit(service) + "\0" + boot()).encode("ascii")).hexdigest()
    return {"id": identity, "monitorId": monitor_identity, "image": digest(pid) if running else None,
            "running": running, "oom": values["Result"] == "oom-kill", "restarts": int(values["NRestarts"]),
            "startedAt": started, "pid": pid}
