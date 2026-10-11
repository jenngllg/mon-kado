#!/usr/bin/env bash
# Verify public Go executables against fresh native PostgreSQL, collecting real process coverage.
set -Eeuo pipefail
[[ $EUID -eq 0 ]] || { echo 'Native process fixtures require root.' >&2; exit 1; }
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd -- "$source_root"
command -v go >/dev/null
command -v setpriv >/dev/null
test -x /usr/lib/postgresql/18/bin/initdb
binary_root=$(mktemp -d /tmp/mk-binaries-XXXXXXXX)
coverage_root=$(mktemp -d /tmp/mk-coverage-XXXXXXXX)
chmod 0711 "$coverage_root"
for app in api worker migrate; do
    CGO_ENABLED=0 go build -cover -coverpkg=github.com/JennGllg/mon-kado/... -o "$binary_root/$app" "./cmd/$app"
done
MONKADO_NATIVE_BINARIES="$binary_root" MONKADO_NATIVE_COVERAGE="$coverage_root" \
    python3 -m unittest discover -s tests/Native.ProcessTests
go tool covdata textfmt -i="$coverage_root/api,$coverage_root/worker,$coverage_root/migrate" -o="${1:-coverage.native.out}"
# Keep the small private fixture binaries and counters for diagnosis; they contain no real credentials.
