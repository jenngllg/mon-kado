"""Small Linux-only helper: changes only the named disposable cgroup, never its parent."""
import json
import sys
from pathlib import Path

from policy import MEMORY, require, run_id


def operate(action, identifier, root=Path("/host-cgroup"), driver="cgroupfs"):
    run_id(identifier)
    require(driver in ("cgroupfs", "systemd"), "INVALID_CGROUP_DRIVER")
    group = root / (identifier if driver == "cgroupfs" else identifier.replace("-", "") + ".slice")
    require(action in ("create", "configure", "status", "remove"), "INVALID_CGROUP_ACTION")
    if action == "create":
        require(not group.exists(), "CGROUP_ALREADY_EXISTS")
        require({"cpu", "memory"} <= set((root / "cgroup.subtree_control").read_text().split()), "CGROUP_UNSUPPORTED")
        group.mkdir()
    if action in ("create", "configure"):
        require(group.is_dir(), "CGROUP_MISSING")
        (group / "memory.max").write_text(str(MEMORY))
        (group / "memory.swap.max").write_text("0")
        (group / "cpu.max").write_text("100000 100000")
        (group / "cgroup.subtree_control").write_text("+cpu +memory")
    if action == "remove":
        # rmdir refuses populated groups and child groups; no recursive removal is permitted.
        if group.exists():
            group.rmdir()
        return {"removed": True}
    memory = int((group / "memory.max").read_text())
    swap = int((group / "memory.swap.max").read_text())
    cpu = (group / "cpu.max").read_text().strip()
    events = dict(line.split() for line in (group / "memory.events").read_text().splitlines())
    return {"qualified": memory == MEMORY and swap == 0 and cpu == "100000 100000",
            "memoryMax": memory, "swapMax": swap, "cpuMax": cpu,
            "memoryCurrent": int((group / "memory.current").read_text()),
            "memoryPeak": int((group / "memory.peak").read_text()),
            "oom": int(events.get("oom_kill", 0)),
            "cpu": dict(line.split() for line in (group / "cpu.stat").read_text().splitlines())}


if __name__ == "__main__":
    print(json.dumps(operate(sys.argv[1], sys.argv[2], driver=sys.argv[3] if len(sys.argv) > 3 else "cgroupfs")))
