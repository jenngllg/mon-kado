#!/usr/bin/env bash
# Build and scan the reviewed edge server without installing or starting a host service.
set -Eeuo pipefail
test "$(uname -m)" = x86_64
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
build_root="$source_root/deployments/caddy/build"
tool_directory="${RUNNER_TEMP:?RUNNER_TEMP must name a private build directory}/monkado-native-tools"
mkdir -p "$tool_directory"
expected_go="go$(awk '$1 == "go" { print $2 }' "$build_root/go.mod")"
test "$(go env GOVERSION)" = "$expected_go"
go -C "$build_root" mod verify
CGO_ENABLED=0 go -C "$build_root" build -mod=readonly -buildvcs=false -trimpath -ldflags='-s -w' -o "$tool_directory/caddy" .
"$tool_directory/caddy" version | grep -E '^v2\.11\.7 '
go version -m "$tool_directory/caddy" | sed -n '1p' | grep -F "$expected_go"
GOBIN="$tool_directory" go install golang.org/x/vuln/cmd/govulncheck@v1.9.0
"$tool_directory/govulncheck" -C "$build_root" ./...
"$tool_directory/govulncheck" -mode=binary -json "$tool_directory/caddy" > "$tool_directory/caddy-vulnerabilities.json"
go -C "$build_root" list -mod=readonly -deps . > "$tool_directory/caddy-dependencies.txt"
python3 "$source_root/scripts/check_caddy_scan.py" "$tool_directory/caddy-vulnerabilities.json" "$tool_directory/caddy-dependencies.txt"
(cd -- "$tool_directory" && sha256sum caddy > caddy.sha256)
if [[ -n ${GITHUB_PATH:-} ]]; then
    echo "$tool_directory" >> "$GITHUB_PATH"
fi
