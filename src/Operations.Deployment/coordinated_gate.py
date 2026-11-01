"""Fail-closed public HTTP isolation for coordinated native rollouts."""

import json
import subprocess

from deploy_policy import DeploymentError, ensure

TABLE = "monkado_transition"
NFT = "/usr/sbin/nft"
CREATE = b"""add table inet monkado_transition
add chain inet monkado_transition input { type filter hook input priority -10; policy accept; }
add rule inet monkado_transition input iifname != lo tcp dport { 80, 443 } reject with tcp reset
"""
DELETE = b"delete table inet monkado_transition\n"
EXPECTED_EXPRESSION = [
    {"match": {"op": "!=", "left": {"meta": {"key": "iifname"}}, "right": "lo"}},
    {"match": {"op": "==", "left": {"payload": {"protocol": "tcp", "field": "dport"}},
               "right": {"set": [80, 443]}}},
    {"reject": {"type": "tcp reset"}},
]


def command(arguments, content=None):
    """Execute nft without a shell and retain no provider output on failure."""
    try:
        result = subprocess.run([NFT, *arguments], input=content, capture_output=True, timeout=15, check=False)
    except (OSError, subprocess.TimeoutExpired):
        raise DeploymentError("TRAFFIC_GATE_UNAVAILABLE") from None
    ensure(result.returncode == 0 and len(result.stdout) <= 131072, "TRAFFIC_GATE_UNAVAILABLE")
    return result.stdout


class Gate:
    """Operate only a dedicated, fully verified nftables table.

    Closing blocks non-loopback HTTP/HTTPS packets without changing SSH or the
    existing UFW rules. A boot-time unit must close it whenever an intent exists;
    the gate alone is not a complete deployment or recovery mechanism.
    """

    def __init__(self, run=command):
        self.run = run

    def exists(self):
        """Fail if the kernel cannot enumerate tables, rather than assuming open."""
        data = json.loads(self.run(["--json", "list", "tables"]))
        ensure(isinstance(data, dict) and isinstance(data.get("nftables"), list), "TRAFFIC_GATE_INVALID")
        return any(isinstance(item, dict) and isinstance(item.get("table"), dict) and
                   item["table"].get("family") == "inet" and item["table"].get("name") == TABLE
                   for item in data["nftables"])

    def verify(self):
        """Accept exactly one expected chain and rule, never a similarly named table."""
        data = json.loads(self.run(["--json", "list", "table", "inet", TABLE]))
        entries = data.get("nftables") if isinstance(data, dict) else None
        ensure(isinstance(entries, list) and len(entries) == 4, "TRAFFIC_GATE_INVALID")
        ensure(all(isinstance(item, dict) and len(item) == 1 for item in entries), "TRAFFIC_GATE_INVALID")
        indexed = {next(iter(item)): next(iter(item.values())) for item in entries}
        ensure(set(indexed) == {"metainfo", "table", "chain", "rule"}, "TRAFFIC_GATE_INVALID")
        table, chain, rule = indexed["table"], indexed["chain"], indexed["rule"]
        ensure(all(isinstance(item, dict) for item in (table, chain, rule)), "TRAFFIC_GATE_INVALID")
        ensure(table.get("family") == "inet" and table.get("name") == TABLE, "TRAFFIC_GATE_INVALID")
        ensure(chain.get("family") == "inet" and chain.get("table") == TABLE and chain.get("name") == "input" and
               chain.get("type") == "filter" and chain.get("hook") == "input" and chain.get("prio") == -10 and
               chain.get("policy") == "accept", "TRAFFIC_GATE_INVALID")
        ensure(rule.get("family") == "inet" and rule.get("table") == TABLE and rule.get("chain") == "input" and
               rule.get("expr") == EXPECTED_EXPRESSION, "TRAFFIC_GATE_INVALID")

    def close(self):
        """Atomically block public traffic and verify the resulting kernel rule."""
        if not self.exists():
            self.run(["-f", "-"], CREATE)
        self.verify()

    def open(self):
        """Remove only the exact checked table after the caller verifies a pair."""
        ensure(self.exists(), "TRAFFIC_GATE_MISSING")
        self.verify()
        self.run(["-f", "-"], DELETE)
        ensure(not self.exists(), "TRAFFIC_GATE_INVALID")
