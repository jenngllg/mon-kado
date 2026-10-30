"""Verify dedicated firewall rules without changing the host firewall."""

import copy
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Deployment"))
from coordinated_gate import CREATE, DELETE, EXPECTED_EXPRESSION, Gate
from deploy_policy import DeploymentError


def table():
    return {"nftables": [{"metainfo": {}}, {"table": {"family": "inet", "name": "monkado_transition"}},
            {"chain": {"family": "inet", "table": "monkado_transition", "name": "input", "type": "filter",
                       "hook": "input", "prio": -10, "policy": "accept"}},
            {"rule": {"family": "inet", "table": "monkado_transition", "chain": "input",
                      "expr": copy.deepcopy(EXPECTED_EXPRESSION)}}]}


class FakeNft:
    def __init__(self):
        self.value = None
        self.calls = []

    def __call__(self, arguments, content=None):
        self.calls.append((arguments, content))
        if arguments == ["--json", "list", "tables"]:
            items = [{"metainfo": {}}]
            if self.value is not None:
                items.append({"table": {"family": "inet", "name": "monkado_transition"}})
            return json.dumps({"nftables": items}).encode()
        if arguments == ["--json", "list", "table", "inet", "monkado_transition"]:
            return json.dumps(self.value).encode()
        if arguments == ["-f", "-"] and content == CREATE:
            self.value = table()
            return b""
        if arguments == ["-f", "-"] and content == DELETE:
            self.value = None
            return b""
        raise AssertionError("unexpected nft operation")


class CoordinatedGateTests(unittest.TestCase):
    def test_close_when_open_creates_exact_table_and_is_idempotent(self):
        nft = FakeNft(); gate = Gate(nft)
        gate.close(); gate.close()
        self.assertTrue(gate.exists())
        self.assertEqual(sum(content == CREATE for _, content in nft.calls), 1)

    def test_open_when_table_is_valid_deletes_only_dedicated_table(self):
        nft = FakeNft(); gate = Gate(nft); gate.close()
        gate.open()
        self.assertFalse(gate.exists())
        self.assertEqual(sum(content == DELETE for _, content in nft.calls), 1)

    def test_open_when_rule_was_changed_does_not_delete_it(self):
        for mutation in ("rule", "chain", "extra"):
            with self.subTest(mutation=mutation):
                nft = FakeNft(); gate = Gate(nft); gate.close()
                if mutation == "rule":
                    nft.value["nftables"][3]["rule"]["expr"][1]["match"]["right"]["set"] = [80]
                elif mutation == "chain":
                    nft.value["nftables"][2]["chain"]["policy"] = "drop"
                else:
                    nft.value["nftables"].append({"rule": {}})
                with self.assertRaises(DeploymentError):
                    gate.open()
                self.assertIsNotNone(nft.value)
                self.assertFalse(any(content == DELETE for _, content in nft.calls))

    def test_open_when_table_is_missing_refuses(self):
        nft = FakeNft()
        with self.assertRaisesRegex(DeploymentError, "TRAFFIC_GATE_MISSING"):
            Gate(nft).open()
        self.assertEqual(len(nft.calls), 1)


if __name__ == "__main__":
    unittest.main()
