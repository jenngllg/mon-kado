#!/usr/bin/env bash
# Execute reviewed local code; MK-813's outer coordination descriptor remains inherited.
set -Eeuo pipefail
umask 077
[[ $EUID -eq 0 ]] || { echo 'Run with sudo.' >&2; exit 1; }
exec env -i PATH=/usr/bin:/bin:/usr/sbin:/sbin LANG=C.UTF-8 \
    /usr/bin/python3 /opt/monkado/src/Operations.Deployment/deploy_cli.py deploy
