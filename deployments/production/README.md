# Production backend deployment (MK-812 / MK-820)

## Contract and prerequisites

The existing DigitalOcean Basic VPS runs Ubuntu 24.04, Docker Engine and Compose.
The public API hostname is `api.monkado.fr`; the frontend origin is
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

Copy the complete reviewed source bundle to an operator-owned staging folder,
preserving its relative paths. Follow [the MK-820 upgrade runbook](rollback-smoke.md)
before the first MK-820 publication. Run
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

## Subsequent approved publications

1. Complete the one-time MK-820 smoke-account handoff, backup upgrade and explicit
   adoption described in the runbook, then get green checks on develop.
2. Dispatch publication and approve it. Declare rollback compatibility only after
   reviewing data/file compatibility with the preceding application. Anonymous
   GHCR pulls must be available.
3. Run `sudo systemctl start monkado-deploy.service` on the VPS.
4. Inspect `sudo systemctl status monkado-deploy.service` and HTTPS `/readiness`.
   Verify the Worker remains running and perform real email checks before accepting
   users. Do not call the site production-ready while the frontend is unavailable.
5. After explicit approval of unattended pulls, enable the free local timer with
   `sudo systemctl enable --now monkado-deploy.timer`. It checks every five minutes
   and applies only explicitly published releases, not every develop commit.

Rollout pulls candidate application images first, stops Caddy/API/Worker for a
short maintenance window, runs the migration bundle once only when new migrations
are present, and recreates API/Worker. PostgreSQL and the existing Caddy container
are retained; Caddy is restarted, not recreated.
The `mon-kado` Compose project name is stable so named volumes survive upgrades.
Never use `down --volumes`, delete volumes, or prune volumes during deployment.
Coordination locks prevent overlap with backups and other deployments. Successful
technical and authenticated persistence smoke tests save
the applied references to `/var/lib/monkado-deployment/current.env` atomically.

The durable `status.json` is authoritative. A failed compatible, schema-unchanged
publication gets one verified automatic application rollback. An interrupted
migration or failed rollback preserves `in-progress.env` and requires explicit
operator recovery. Never delete a marker to retry or roll a database backwards.
Failed publication IDs are not retried: publish again with a new approval.
Retain the previous image references; there is no automatic image/volume pruning.
Monitor disk
usage and remove only reviewed unused images before the 25 GB disk fills up.

## Small VPS limits and remaining work

All five services share `monkado.slice`, with a total 768 MiB memory limit and no
swap. This avoids the fixed API limit that failed image uploads in local tests.
The API/migration managed heap is capped at 256 MiB, the Worker at 192 MiB; native
image memory is additional. PostgreSQL uses 64 MiB shared buffers, 40 connections,
and application pools of 12. These are initial settings to verify on the real VPS,
not a throughput guarantee. Do not compile on this machine. Previous local image
stress tests reached the shared limit: uploads/exports still need load validation.

The shared persistence registration retains at most 32 EF Core contexts after a
traffic burst, instead of the provider's default pool capacity of 1024. This is
independent of the 12-connection PostgreSQL pool and does not reject simultaneous
requests above 32: overflow contexts are created normally and disposed rather
than retained. The scoped context and `IUnitOfWork` remain the same instance, and
tracked state is reset before reuse. Repeated bursts above this capacity can add
context-construction work; verify latency under the intended load. This is a
post-burst retention safeguard, not a promised idle-memory reduction or evidence
that the earlier alert was caused by this pool.

Public social-image previews now share the same process-wide native-image admission
limit as wish/profile uploads and merchant imports: one operation runs, at most two
wait, and admission expires after five seconds. Overflow returns the documented
non-cacheable `429` response before database/image buffers are allocated. This
prevents independent crawler requests from multiplying native image allocations;
it is not a claim that the complete deployment is ready for unbounded traffic.

API images generate the `v1` OpenAPI document during publishing, using the standard
`Microsoft.Extensions.ApiDescription.Server` build tooling with synthetic settings
and an unreachable database. The immutable JSON is served at `/openapi/v1.json`
through the existing security, CORS and correlation middleware. Published images set
`OpenApi__DocumentPath=/app/openapi/v1.json`; a missing artifact fails startup rather
than falling back to runtime generation. No production credentials or member data
are used to produce the documentation. An empty OpenAPI `servers` collection uses
the document's current origin rather than persisting the synthetic build origin.
Local source runs and contract tests retain dynamic generation when no artifact
path is configured. Technical deployment checks still validate the same public URL.
The packaging check verifies that the response matches the embedded artifact,
security headers, unsupported document names, HEAD and authentication enforcement.

This release's API configuration pins glibc's `MALLOC_MMAP_THRESHOLD_` to 1 MiB. Large native image
allocations can then be released back to the OS rather than changing the dynamic
threshold and retaining subsequent buffers in allocator arenas. This complements
the managed heap cap; it does not cap all native memory or replace correct disposal.
The setting is specific to the current Ubuntu/glibc image and is deliberately not
applied to the Worker, PostgreSQL or Caddy.

In isolated one-CPU-quota tests on .NET 10.0.12, the same application image handled
1,200 reads, five 36-megapixel PNG uploads, and 720 successful social previews.
Two default-allocator runs ended at approximately 249 and 283 MiB API PSS;
128-KiB threshold runs ended at 178 and 181 MiB. A 1-MiB threshold run ended at
180 MiB. PSS attributes shared resident pages proportionally rather than adding
RSS across processes. No OOM kill occurred. These are local synthetic measurements,
not production capacity guarantees or proof that every possible leak is absent.

The local host exposes 12 native online CPUs despite the quota, whereas the VPS
exposes one. An additional comparison pins `MALLOC_ARENA_MAX=8` on both candidates
to reproduce glibc's one-CPU arena ceiling. It confirms the reduction: 252 MiB
default versus 178 MiB at the 1-MiB threshold. This arena setting is a test control,
not a new production override.

The setting has a performance tradeoff. In a measured run, the mixed 12/3-client
preview workload had mean/p95 latency of 130/301 ms with the default allocator,
201/488 ms at 128 KiB, and 164/386 ms at 1 MiB. Upload/read latency was comparable.
The 1-MiB value retains most of the observed memory benefit with less overhead than
128 KiB. Repeat these checks after changing the runtime/native image library.
With the eight-arena test control, mean/p95 preview latency was 122/287 ms default
and 211/492 ms at 1 MiB. Do not describe the memory reduction as a free throughput
improvement; production warm-memory and load validation remain required.
A 4-MiB candidate with the same eight-arena control ended at 193 MiB, with
187/422 ms mean/p95 preview latency. It recovered less memory and did not eliminate
the load-time overhead, so the prepared release retains the 1-MiB setting.

The combined admission-guard/1-MiB test admitted 31 of 360 requests at 12-client
concurrency and rejected the rest with `429`; a following 360 requests at three
clients and the final HEAD request all succeeded. API PSS ended at 184 MiB with no
OOM kill. Do not count rejected requests as a throughput or latency improvement.

Publishing this configuration still requires the reviewed reinstall described
above because it changes the deployment configuration hash. After rollout, verify
the exact API environment setting with an allowlisted inspection, readiness and
image processing, and compare warm memory over time, not only immediately after
restart. To revert, remove only this API setting from the reviewed Compose overlay
and recreate the API through the normal rollout; do not force GC, drop OS caches,
resize the VPS or remove volumes. No extra paid service is required.

See [glibc memory-allocation tunables](https://sourceware.org/glibc/manual/latest/html_node/Memory-Allocation-Tunables.html)
for static threshold and release semantics.
The [glibc 2.39 allocator implementation](https://raw.githubusercontent.com/bminor/glibc/glibc-2.39/malloc/malloc.c)
also shows how adaptive mmap threshold increases raise the trimming threshold,
and how an explicit threshold disables that adaptation. The experiments support
allocator retention during image processing; they do not establish the exclusive
cause of the earlier production alert or demonstrate a missing `Dispose`.

The persistent volumes include PostgreSQL, images/profile photos, Data Protection
keys, exports and Caddy certificate state. Persistent volumes are NOT backups.
Off-server backup/restore (including images/keys) is maintained by MK-813; security
verification is MK-814, monitoring/alerts MK-815 and performance testing MK-816.
Do not import real personal data before backups and restore are verified. OS
security updates and any required reboot must be scheduled before opening service.

References: [Docker installation](https://docs.docker.com/engine/install/ubuntu/),
[Docker/UFW limitations](https://docs.docker.com/engine/network/packet-filtering-firewalls/),
[GHCR visibility and digests](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).
