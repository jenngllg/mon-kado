# Native production backend deployment

The backend consists of three Go executables (API, worker, explicit SQL migrations),
PostgreSQL 18 and Caddy on Ubuntu 24.04. No Docker daemon, image registry, .NET runtime,
container volume or migration bundle is required. Only Caddy exposes TCP 80/443;
PostgreSQL and the API listen locally. Restrict SSH to the operator's current IP.

## Migration status

Native publication, deployment transitions, adoption, smoke tests and supervision
have local automated tests, including an isolated real systemd restart/OOM exercise.
Backup/restore, security probes and performance campaigns still require their native
reconciliation before the final migration merge. Subsequent deployment deliberately
checks the installed backup configuration inventory and refuses an older bundle.
No production deployment or production qualification has been performed.

## Protected publication

The manual `Publish approved backend` workflow runs only on `develop`, behind the
existing `production` environment's required reviewers. CI and CodeQL must succeed
for the exact revision. The workflow builds static Linux amd64 executables, verifies
embedded source metadata and the complete SQL catalog, and publishes a new immutable
`backend-<revision>-<runId>-<attempt>` GitHub release. Manifest schema 3 binds all three
asset URLs, SHA-256 hashes, sizes, source revision, publication ID, SQL catalog,
configuration fingerprint and the explicit rollback declaration.

The fixed `backend-production` release body is updated only after the immutable
assets are published. The server downloads only approved executable bytes; it never
runs operational scripts from a release. Installed root-owned operational code checks
HTTPS redirects, byte limits and every hash before executing metadata commands.
Cached and retained executables are reverified before reuse. No deployment PAT,
self-hosted runner or inbound GitHub SSH access is needed.

## Native host preparation

Install native PostgreSQL 18 and Caddy using their reviewed official packages. Create
the non-login `monkado` system account. Configure PostgreSQL to listen on loopback,
with database/role `mon_kado`, a privately generated password, 64 MB shared buffers,
40 connections and 2 MB work memory. The application pool is bounded. Preserve local
peer access for the `postgres` Unix user; operations use it without password arguments.
Do not grant the application superuser, role creation or database creation rights.

Review the complete source bundle and run `sudo bash deployments/production/install.sh`.
The installer leaves deployment, frontend and monitor timers stopped. It installs
root-owned code and native units; it does not start the application, replace secrets,
install a database or restart existing PostgreSQL/Caddy processes. Do not invoke it
while an operational job is active. A configuration hash mismatch requires a reviewed
reinstall, never a downloaded configuration override.

Privately create `/etc/monkado/production.env` and `/etc/monkado/caddy.env`, root:root
0600, from the examples. These are systemd EnvironmentFiles, never shell scripts.
The database URL must have this exact target (substitute only the password privately):
`postgres://mon_kado:PASSWORD@127.0.0.1:5432/mon_kado?sslmode=disable`.
Generate independent 32-byte Base64 JWT and data-encryption keys and retain them
across releases. Configure Gmail privately; Google login stays disabled until its
separate provider setup is approved. Caddy receives only host/static-path settings.
Its unit deliberately omits `--environ`, which would disclose environment values.

For the frontend, configure its host and paths, then install the reviewed
`deployments/frontend/frontend.caddy` as
`/opt/monkado/deployments/caddy/frontend/site.caddy`. The frontend pointer is
`/var/lib/monkado-frontend/releases/current`. Complete the frontend runbook before
setting `frontendEnabled` in monitoring; publication timers remain opt-in.

## First native installation

1. Approve and publish the native release. Transfer its exact `backend-manifest.json`
   privately to `/etc/monkado/initial-manifest.json`, root:root 0600.
2. Run `sudo monkado-deploy initialize` only against an empty native database.
   It refuses existing application tables/processes, verifies the publication, persists
   maintenance intent, runs SQL once and starts verified executables. Readiness alone
   leaves the installation awaiting adoption; interrupted intent is never replayed.
3. Register and confirm a dedicated ordinary smoke-test member through the normal
   application. Retain its password and immutable member ID privately, separately
   from backup storage. Run `sudo monkado-deploy provision-smoke`; inputs are masked.
4. Run `sudo monkado-deploy adopt /etc/monkado/initial-manifest.json`.
   Adoption requires the exact approved publication, binary digests/metadata,
   complete SQL history, fresh telemetry and both technical and member CRUD smokes.
   It commits the baseline and removes maintenance intent only after all checks pass.
5. Complete native backup/restore qualification before enabling subsequent deployment
   or accepting data to preserve. Then run `sudo monkado-deploy check-config` and
   inspect `sudo monkado-deploy status`.

If initialization fails, investigate the retained state locally. Do not delete its
marker, rerun initialization over existing data or perform a down migration. After
manually repairing the exact approved installation, adoption can verify it without
replaying SQL. A different publication cannot silently replace interrupted intent.

## Subsequent releases and resource limits

Run `sudo systemctl start monkado-deploy.service` after protected publication.
Preflight downloads and verifies the candidate before maintenance, checks backups,
free disk, dedicated member credentials, retained binaries, infrastructure and frontend.
Caddy/API/worker stop briefly; PostgreSQL remains running. SQL executes only for an
append-only catalog. The executable pointer changes atomically; private `active.env`
sets the approved log revision. No build occurs on the VPS.

All four native services share `monkado.slice`: 768 MiB, no swap. Go's soft memory
limits are 256 MiB for API and 192 MiB for worker. These are initial settings requiring
native load qualification, not a throughput guarantee. The operational job's cap is
20 minutes. Only explicitly approved releases are pulled if the timer is later enabled.

`status.json` is authoritative; `current.env` and `in-progress.env` contain only
non-secret digests/revision. Retained immutable executables permit a verified
application rollback only when the SQL catalog is unchanged and compatibility was
explicitly declared. Database rollback is never automatic. See [recovery](rollback-smoke.md).
