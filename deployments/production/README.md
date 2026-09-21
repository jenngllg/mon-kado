# Production backend deployment (MK-812)

## Contract and prerequisites

The existing DigitalOcean Basic VPS runs Ubuntu 24.04, Docker Engine and Compose.
The public API hostname is `api.monkado.fr`; the future frontend origin is
`https://www.monkado.fr`. Only Caddy publishes TCP 80/443. SSH is restricted to the
operator's current IP by the free DigitalOcean firewall. Never publish PostgreSQL
or the API container port, and never open SSH globally for GitHub-hosted runners.

GitHub Actions builds and scans API/Worker images on a standard hosted runner for
this public repository. It publishes to GHCR, then updates the JSON body of the
`backend-production` release. The VPS makes outbound HTTPS requests only. It
downloads pinned image digests, not code, Compose files or shell commands from a
release. No self-hosted runner, PAT or SSH private key is installed for deployment.
No paid cloud products are required; do not enable billing overages or paid runners.

Before publication, configure a GitHub environment named `production` with Jenn
as required reviewer and a branch restriction to `develop`. The workflow refuses
publication without a required reviewer and successful **CI and Containers runs
for the exact commit**. If Containers was skipped by path filters, dispatch it on
develop first. Publication itself is manual (`Publish approved backend`). A green
publication does not prove the VPS rollout succeeded.

GHCR packages are initially private. After their first publication, explicitly
review and make `mon-kado-api` and `mon-kado-worker` public (the source repository
is already public). Do not publish images containing credentials. Anonymous pulls
must succeed before rollout; otherwise deployment fails before stopping services.
Do not add a broad GitHub token to the VPS as a workaround.

## Install reviewed configuration

Copy the reviewed `compose.yaml`, `deployments/caddy/Caddyfile`, and this directory
to an operator-owned staging folder, preserving their relative paths. Run
`sudo bash deployments/production/install.sh` from that bundle. It installs code
under `/opt/monkado` owned by root and stops (does not enable) the deployment timer.
Changing any file covered by `release_manifest.py` requires a reviewed reinstall:
the release's configuration hash must match the installed files before rollout.
The `backend-production` tag identifies the channel, not the currently deployed
commit; the validated `revision` and digests in its body identify that commit.

Create `/etc/monkado/production.env`, owner root, permissions 0600, using
`production.env.example`. Generate a unique PostgreSQL password and 32-byte
Base64 JWT key. Fill Gmail credentials through a private local/SSH path; never
paste them into a ticket, commit, workflow, log or chat. Google login remains
disabled until its production client and real HTTPS smoke test are approved.
Production deliberately refuses a disabled email provider. Do not switch to
the Local environment to bypass this validation.

The service reads dotenv values through Compose, never through shell `source`.
Do not run `docker compose config` without `--quiet` or publish `docker inspect`
output: both may reveal container credentials. Protect Docker access like root
access; this installation does not grant the `docker` group or passwordless sudo.

## First rollout and subsequent publications

1. Complete the production environment settings and get green checks on develop.
2. Dispatch publication and approve it. Verify anonymous GHCR pulls are available.
3. Run `sudo systemctl start monkado-deploy.service` on the VPS.
4. Inspect `sudo systemctl status monkado-deploy.service` and HTTPS `/readiness`.
   Verify the Worker remains running and perform real email checks before accepting
   users. Do not call the site production-ready while the frontend is unavailable.
5. After explicit approval of unattended pulls, enable the free local timer with
   `sudo systemctl enable --now monkado-deploy.timer`. It checks every five minutes
   and applies only explicitly published releases, not every develop commit.

Rollout pulls all images first, stops Caddy/API/Worker for a short maintenance
window, starts PostgreSQL, runs the migration bundle once, and recreates services.
The `mon-kado` Compose project name is stable so named volumes survive upgrades.
Never use `down --volumes`, delete volumes, or prune volumes during deployment.
An advisory lock prevents overlapping deployments. Successful readiness saves
the applied references to `/var/lib/monkado-deployment/current.env` atomically.

Failures after the maintenance window preserve `in-progress.env` and stop future
rollouts. Inspect locally, resolve the cause and only then remove that marker to
retry. Never blindly roll a database backwards. Automated rollback and richer
end-to-end smoke tests belong to MK-820. Retain the previous image references;
there is deliberately no automatic image/volume pruning in this US. Monitor disk
usage and remove only reviewed unused images before the 25 GB disk fills up.

## Small VPS limits and remaining work

All five services share `monkado.slice`, with a total 768 MiB memory limit and no
swap. This avoids the fixed API limit that failed image uploads in local tests.
The API/migration managed heap is capped at 256 MiB, the Worker at 192 MiB; native
image memory is additional. PostgreSQL uses 64 MiB shared buffers, 40 connections,
and application pools of 12. These are initial settings to verify on the real VPS,
not a throughput guarantee. Do not compile on this machine. Previous local image
stress tests reached the shared limit: uploads/exports still need load validation.

The persistent volumes include PostgreSQL, images/profile photos, Data Protection
keys, exports and Caddy certificate state. Persistent volumes are NOT backups.
Off-server backup/restore (including images/keys) remains MK-813; security rollout
verification is MK-814, monitoring/alerts MK-815 and performance testing MK-816.
Do not import real personal data before backups and restore are verified. OS
security updates and any required reboot must be scheduled before opening service.

References: [Docker installation](https://docs.docker.com/engine/install/ubuntu/),
[Docker/UFW limitations](https://docs.docker.com/engine/network/packet-filtering-firewalls/),
[GHCR visibility and digests](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).
