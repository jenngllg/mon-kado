"""Fail closed on edge binary findings; qualify only the unimported OpenPGP wildcard advisory."""
import json
from pathlib import Path
import sys

OPENPGP_ADVISORY = "GO-2026-5932"
OPENPGP_PACKAGE = "golang.org/x/crypto/openpgp"


def verify(stream, dependencies):
    """Reject findings unless every affected package is demonstrably absent from the build graph."""
    decoder, offset, ignored, configured = json.JSONDecoder(), 0, set(), False
    while offset < len(stream):
        while offset < len(stream) and stream[offset].isspace():
            offset += 1
        if offset == len(stream):
            break
        event, offset = decoder.raw_decode(stream, offset)
        if "config" in event:
            config = event["config"]
            configured = config.get("scanner_name") == "govulncheck" and config.get("scan_mode") == "binary" and config.get("scan_level") == "symbol"
        if "finding" not in event:
            continue
        finding = event["finding"]
        if finding["osv"] != OPENPGP_ADVISORY:
            raise ValueError("CADDY_BINARY_VULNERABILITY")
        if any(name == OPENPGP_PACKAGE or name.startswith(OPENPGP_PACKAGE + "/") for name in dependencies):
            raise ValueError("CADDY_IMPORTS_UNMAINTAINED_OPENPGP")
        for frame in finding["trace"]:
            package = frame.get("package")
            if package is not None and package != OPENPGP_PACKAGE and not package.startswith(OPENPGP_PACKAGE + "/"):
                raise ValueError("CADDY_UNEXPECTED_ADVISORY_PACKAGE")
            function = frame.get("function")
            qualified = frame.get("receiver", "") + "." + str(function)
            if function not in (None, "*") and qualified != str(package) + "/*":
                raise ValueError("CADDY_BINARY_VULNERABILITY")
        ignored.add(OPENPGP_ADVISORY)
    if not configured:
        raise ValueError("CADDY_BINARY_SCAN_MISSING")
    return sorted(ignored)


def main():
    """Read scanner output and the matching go list dependency graph, never provider or host settings."""
    dependencies = set(Path(sys.argv[2]).read_text(encoding="utf-8-sig").splitlines())
    if "github.com/caddyserver/caddy/v2" not in dependencies:
        raise ValueError("CADDY_DEPENDENCY_GRAPH_MISSING")
    qualified = verify(Path(sys.argv[1]).read_text(encoding="utf-8-sig"), dependencies)
    print(json.dumps({"executableVulnerabilities": 0, "unimportedWildcardAdvisories": qualified}))


if __name__ == "__main__":
    main()
