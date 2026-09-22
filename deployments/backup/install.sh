#!/usr/bin/env bash
# Install reviewed code only; never authorize Drive or enable captures implicitly.
set -Eeuo pipefail
umask 077
[[ $EUID -eq 0 ]] || { echo 'Run with sudo.' >&2; exit 1; }
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
python3 -c 'import sys; assert sys.version_info >= (3, 12)'
command -v restic >/dev/null
command -v rclone >/dev/null
systemctl is-active --quiet docker
install -d -m 0700 /etc/monkado-backup /var/lib/monkado-backup
exec 9>/var/lib/monkado-backup/backup.lock
flock -n 9 || { echo 'A backup operation is active.' >&2; exit 1; }
exec 8>/var/lib/monkado-deployment/deploy.lock
flock -n 8 || { echo 'A deployment is active. Retry installation after it finishes.' >&2; exit 1; }
install -d -m 0755 /opt/monkado-backup/monkado_backup
for module in __init__ policy capture operations cli restore credentials; do
    install -m 0644 "$source_root/src/Operations.Backups/monkado_backup/$module.py" "/opt/monkado-backup/monkado_backup/$module.py"
done
install -m 0755 "$source_root/deployments/backup/monkado-backup" /usr/local/sbin/monkado-backup
install -m 0755 "$source_root/deployments/backup/monkado-guarded-deploy" /usr/local/sbin/monkado-guarded-deploy
install -d -m 0755 /etc/systemd/system/monkado-deploy.service.d
install -m 0644 "$source_root/deployments/backup/deployment-guard.conf" /etc/systemd/system/monkado-deploy.service.d/backup-guard.conf
for unit in monkado-backup.service monkado-backup.timer monkado-backup-check.service monkado-backup-check.timer monkado-backup-recovery.service monkado-backup-transfer.service; do
    install -m 0644 "$source_root/deployments/backup/$unit" "/etc/systemd/system/$unit"
done
systemctl daemon-reload
systemctl enable monkado-backup-recovery.service
echo 'Backup code installed. Timers have not been enabled.'
echo 'Complete the credential handoff and isolated restoration before enabling captures.'
