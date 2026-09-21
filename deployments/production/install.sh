#!/usr/bin/env bash
# Run manually after reviewing this bundle. Never enables unattended deployments.
set -Eeuo pipefail
umask 077
[[ $EUID -eq 0 ]] || { echo 'Run with sudo.' >&2; exit 1; }
# shellcheck source=/dev/null
source /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 24.04 ]] || exit 1
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
systemctl is-active --quiet docker
command -v python3 >/dev/null
command -v curl >/dev/null
command -v flock >/dev/null
systemctl stop monkado-deploy.timer 2>/dev/null || true
if systemctl is-active --quiet monkado-deploy.service; then
    echo 'Wait for the active deployment before installing configuration.' >&2
    exit 1
fi
install -d -m 0700 /var/lib/monkado-deployment
exec 9>/var/lib/monkado-deployment/deploy.lock
flock -n 9 || { echo 'A deployment holds the configuration lock.' >&2; exit 1; }
install -d -m 0755 /opt/monkado/deployments/caddy /opt/monkado/deployments/production
install -d -m 0700 /etc/monkado /var/lib/monkado-deployment
install -m 0644 "$source_root/compose.yaml" /opt/monkado/compose.yaml
install -m 0644 "$source_root/deployments/caddy/Caddyfile" /opt/monkado/deployments/caddy/Caddyfile
for file in compose.production.yaml release_manifest.py deploy.sh monkado.slice; do
    install -m 0644 "$source_root/deployments/production/$file" "/opt/monkado/deployments/production/$file"
done
for unit in monkado.slice monkado-deploy.service monkado-deploy.timer; do
    install -m 0644 "$source_root/deployments/production/$unit" "/etc/systemd/system/$unit"
done
systemctl daemon-reload
systemctl enable --now monkado.slice
echo 'Deployment code installed. Timer remains stopped; no application has been started.'
echo 'Configure /etc/monkado/production.env (root:root, 0600), approve a publication,'
echo 'then run: sudo systemctl start monkado-deploy.service'
echo 'Enable the timer only after a successful first rollout and review.'
