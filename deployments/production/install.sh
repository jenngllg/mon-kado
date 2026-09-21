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
if systemctl is-active --quiet monkado-monitor.timer || systemctl is-active --quiet monkado-monitor.service; then
    echo 'Stop the monitor timer and wait for its current check before installing reviewed configuration.' >&2
    exit 1
fi
systemctl stop monkado-deploy.timer 2>/dev/null || true
systemctl stop monkado-frontend.timer 2>/dev/null || true
if systemctl is-active --quiet monkado-deploy.service; then
    echo 'Wait for the active deployment before installing configuration.' >&2
    exit 1
fi
if systemctl is-active --quiet monkado-frontend.service; then
    echo 'Wait for the active frontend publication before installing configuration.' >&2
    exit 1
fi
install -d -m 0700 /var/lib/monkado-deployment
exec 8>/var/lib/monkado-deployment/backup-coordination.lock
flock -n 8 || { echo 'A backup operation holds the coordination lock.' >&2; exit 1; }
exec 9>/var/lib/monkado-deployment/deploy.lock
flock -n 9 || { echo 'A deployment holds the configuration lock.' >&2; exit 1; }
install -d -m 0755 /opt/monkado/deployments/caddy /opt/monkado/deployments/production
install -d -m 0755 /opt/monkado/deployments/frontend /opt/monkado/src/Operations.Frontend
install -d -m 0755 /var/lib/monkado-frontend /var/lib/monkado-frontend/releases
install -d -m 0700 /etc/monkado /var/lib/monkado-deployment
install -d -m 0700 /var/lib/monkado-monitoring
exec 7>/var/lib/monkado-monitoring/monitor.lock
flock -n 7 || { echo 'A monitor operation holds its state lock.' >&2; exit 1; }
install -d -m 0755 /opt/monkado/deployments/monitoring /opt/monkado/src/Operations.Monitoring
install -d -m 0755 /var/lib/monkado-observability
install -d -m 0700 -o 1654 -g 1654 /var/lib/monkado-observability/api /var/lib/monkado-observability/worker
install -m 0644 "$source_root/compose.yaml" /opt/monkado/compose.yaml
install -m 0644 "$source_root/deployments/caddy/Caddyfile" /opt/monkado/deployments/caddy/Caddyfile
for file in compose.production.yaml release_manifest.py deploy.sh monkado.slice; do
    install -m 0644 "$source_root/deployments/production/$file" "/opt/monkado/deployments/production/$file"
done
install -m 0644 "$source_root/deployments/frontend/frontend.caddy" /opt/monkado/deployments/frontend/frontend.caddy
for file in frontend_contract.py frontend_runtime.py frontend_cli.py; do
    install -m 0644 "$source_root/src/Operations.Frontend/$file" "/opt/monkado/src/Operations.Frontend/$file"
done
for unit in monkado-frontend.service monkado-frontend.timer; do
    install -m 0644 "$source_root/deployments/frontend/$unit" "/opt/monkado/deployments/frontend/$unit"
    install -m 0644 "$source_root/deployments/frontend/$unit" "/etc/systemd/system/$unit"
done
for unit in monkado.slice monkado-deploy.service monkado-deploy.timer; do
    install -m 0644 "$source_root/deployments/production/$unit" "/etc/systemd/system/$unit"
done
for file in monitor_cli.py monitor_collect.py monitor_gmail.py monitor_policy.py monitor_provision.py monitor_runtime.py monitor_storage.py; do
    install -m 0644 "$source_root/src/Operations.Monitoring/$file" "/opt/monkado/src/Operations.Monitoring/$file"
done
for unit in monkado-monitor.service monkado-monitor.timer; do
    install -m 0644 "$source_root/deployments/monitoring/$unit" "/opt/monkado/deployments/monitoring/$unit"
    install -m 0644 "$source_root/deployments/monitoring/$unit" "/etc/systemd/system/$unit"
done
install -m 0644 "$source_root/deployments/monitoring/monitoring.json.example" /opt/monkado/deployments/monitoring/monitoring.json.example
if [[ ! -e /etc/monkado/monitoring.json && ! -L /etc/monkado/monitoring.json ]]; then
    install -m 0600 "$source_root/deployments/monitoring/monitoring.json.example" /etc/monkado/monitoring.json
fi
systemctl daemon-reload
systemctl enable --now monkado.slice
echo 'Deployment code installed. Timer remains stopped; no application has been started.'
echo 'Configure /etc/monkado/production.env (root:root, 0600), approve a publication,'
echo 'then run: sudo systemctl start monkado-deploy.service'
echo 'Enable the timer only after a successful first rollout and review.'
echo 'Frontend publication remains stopped. Review DNS, HTTPS and the approved frontend release before enabling its timer.'
echo 'Monitoring remains stopped. Existing monitoring settings, credentials and backup units have not been replaced.'
echo 'Review the monitoring runbook, complete the explicitly approved email test, then enable the monitor timer manually.'
