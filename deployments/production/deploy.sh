#!/usr/bin/env bash
# Root-owned, locally installed code only. Never download/execute release scripts.
set -Eeuo pipefail
umask 077
[[ $EUID -eq 0 ]] || { echo 'Run with sudo.' >&2; exit 1; }
root=/opt/monkado
state=/var/lib/monkado-deployment
secrets=/etc/monkado/production.env
[[ -f $secrets && ! -L $secrets ]] || { echo 'Production secrets are missing.' >&2; exit 1; }
[[ $(stat -c '%u:%a' "$secrets") == 0:600 ]] || { echo 'Secrets must be root-owned, mode 0600.' >&2; exit 1; }
install -d -m 0700 "$state"
exec 9>"$state/deploy.lock"
flock -n 9 || { echo 'Another deployment is running.' >&2; exit 1; }
temporary=$(mktemp -d "$state/attempt.XXXXXX")
trap 'rm -f -- "$temporary/release.json" "$temporary/images.env"; rmdir -- "$temporary"' EXIT
curl --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
    --connect-timeout 10 --max-time 30 --max-filesize 65536 \
    https://api.github.com/repos/jenngllg/mon-kado/releases/tags/backend-production \
    -o "$temporary/release.json"
python3 "$root/deployments/production/release_manifest.py" release "$temporary/release.json" > "$temporary/images.env"
# An interrupted migration/rollout requires an operator, not automatic retries.
if [[ -e $state/in-progress.env ]]; then
    echo 'An earlier rollout did not finish. Inspect it before removing in-progress.env.' >&2
    exit 1
fi
if [[ -f $state/current.env ]] && cmp -s "$temporary/images.env" "$state/current.env"; then
    echo 'The approved release is already deployed.'
    exit 0
fi
compose=(env -i PATH=/usr/bin:/bin:/usr/sbin:/sbin docker compose --project-name mon-kado --project-directory "$root"
    --env-file "$secrets" --env-file "$temporary/images.env"
    -f "$root/compose.yaml" -f "$root/deployments/production/compose.production.yaml")
# Do not inherit shell overrides for Compose secrets or deployment references.
unset DOCKER_HOST DOCKER_CONTEXT
"${compose[@]}" config --quiet
"${compose[@]}" pull postgres migrations api worker caddy
systemctl is-active --quiet monkado.slice
install -m 0600 "$temporary/images.env" "$state/in-progress.env"
# A brief maintenance window prevents old workers/API from using a migrated schema.
"${compose[@]}" stop caddy api worker
"${compose[@]}" up -d --no-deps --wait --wait-timeout 120 postgres
"${compose[@]}" run --rm --no-deps migrations
"${compose[@]}" up -d --no-deps --no-build --force-recreate api worker caddy
healthy_samples=0
for _attempt in {1..30}; do
    # Probe this VPS even if public DNS is later changed; keep TLS verification.
    if curl --fail --silent --show-error --max-time 10 --resolve api.monkado.fr:443:127.0.0.1 https://api.monkado.fr/readiness >/dev/null 2>&1; then
        worker_id=$("${compose[@]}" ps -q worker)
        [[ -n $worker_id ]]
        [[ $(docker inspect --format '{{.State.Running}}' "$worker_id") == true ]]
        [[ $(docker inspect --format '{{.RestartCount}}' "$worker_id") == 0 ]]
        healthy_samples=$((healthy_samples + 1))
        if [[ $healthy_samples -ge 3 ]]; then
            mv -- "$state/in-progress.env" "$state/current.env"
            echo 'Approved backend release deployed; HTTPS readiness passed.'
            exit 0
        fi
    else
        healthy_samples=0
    fi
    sleep 5
done
echo 'Readiness failed. Keep in-progress.env; inspect locally without publishing secrets.' >&2
exit 1
