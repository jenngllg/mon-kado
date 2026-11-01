#!/usr/bin/env bash
# A dedicated forced-command key can request only this approved revision.
set -Eeuo pipefail
umask 077
[[ $# -eq 1 && $1 =~ ^[0-9a-f]{40}$ ]]
[[ -n ${VPS_DEPLOY_KEY:-} && -n ${VPS_HOST:-} && -n ${VPS_USER:-} && -n ${VPS_KNOWN_HOSTS:-} ]]
directory=$(mktemp -d)
trap 'rm -f -- "$directory/key" "$directory/known_hosts"; rmdir -- "$directory"' EXIT
printf '%s\n' "$VPS_DEPLOY_KEY" > "$directory/key"
printf '%s\n' "$VPS_KNOWN_HOSTS" > "$directory/known_hosts"
unset VPS_DEPLOY_KEY
ssh -T -i "$directory/key" -o IdentitiesOnly=yes -o BatchMode=yes \
  -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$directory/known_hosts" \
  -o ConnectTimeout=15 -o ServerAliveInterval=15 -o ServerAliveCountMax=4 \
  "$VPS_USER@$VPS_HOST" "$1"
