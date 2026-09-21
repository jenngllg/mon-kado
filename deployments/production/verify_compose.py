"""Assert the resolved deployment contract; do not print interpolated secrets."""

import json
import sys

services = json.load(sys.stdin)["services"]
assert set(services) == {"postgres", "migrations", "api", "worker", "caddy"}
for name, service in services.items():
    assert service["cgroup_parent"] == "monkado.slice", name
    assert "build" not in service, name
    if name != "caddy":
        assert not service.get("ports"), name
    if name in {"api", "worker", "migrations"}:
        assert "@sha256:" in service["image"], name
        assert service["read_only"], name
        assert service["cap_drop"] == ["ALL"], name
        assert "Maximum Pool Size=12" in service["environment"]["ConnectionStrings__PostgreSql"], name
assert {(str(port["published"]), port["target"], port["protocol"]) for port in services["caddy"]["ports"]} == {
    ("80", 80, "tcp"), ("443", 443, "tcp")}
assert services["migrations"]["image"] == services["api"]["image"]
assert services["migrations"]["environment"]["ReverseProxy__KnownNetworks__0"]
for volume in ("gift_images", "data_protection_keys", "personal_data_exports"):
    assert any(item["source"] == volume for item in services["api"]["volumes"])
    assert any(item["source"] == volume for item in services["worker"]["volumes"])
print("Production Compose contract verified.")
