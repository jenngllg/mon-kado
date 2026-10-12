#!/usr/bin/env bash
# Install the official, checksum-verified test Caddy without starting a host service.
set -Eeuo pipefail
test "$(uname -m)" = x86_64
tool_directory="$RUNNER_TEMP/monkado-native-tools"
mkdir -p "$tool_directory"
base_url=https://github.com/caddyserver/caddy/releases/download/v2.11.4
for file in caddy_2.11.4_linux_amd64.tar.gz caddy_2.11.4_checksums.txt; do
    curl --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
        --max-time 120 --output "$tool_directory/$file" "$base_url/$file"
done
(
    cd -- "$tool_directory"
    awk '$2 == "caddy_2.11.4_linux_amd64.tar.gz" { print }' caddy_2.11.4_checksums.txt > expected.sha512
    test "$(wc -l < expected.sha512)" -eq 1
    sha512sum --check --status expected.sha512
    tar -xzf caddy_2.11.4_linux_amd64.tar.gz caddy
    chmod 0755 caddy
    ./caddy version | grep -E '^v2\.11\.4 '
)
echo "$tool_directory" >> "$GITHUB_PATH"
