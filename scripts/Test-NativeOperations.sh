#!/usr/bin/env bash
# Run real local HTTPS and installer journeys inside disposable namespaces.
set -Eeuo pipefail
[[ $EUID -eq 0 ]] || { echo 'Native namespace tests require root.' >&2; exit 1; }
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
for suite in security database-security frontend-http frontend-release installer entrypoint go-smoke; do
    python3 "$source_root/tests/Native.OperationsTests/run.py" "$suite"
done
MONKADO_MONITOR_NATIVE_TESTS=1 \
    PYTHONPATH="$source_root/src/Operations.Monitoring:$source_root/src/Operations.Native:$source_root/tests/Operations.MonitoringTests" \
    python3 -m unittest discover -s "$source_root/tests/Operations.MonitoringTests" -p test_monitor_native.py
