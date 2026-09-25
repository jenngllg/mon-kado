# MK-820 — verified deployment and recovery

This is an operator runbook, **not authorization to install, publish, deploy or
rotate production credentials**. Review the bundle and obtain approval first.
There is no paid component, new public endpoint, business migration, deployment
SSH key, or production credential in GitHub Actions.

## Approval and compatibility

The existing manually dispatched publication workflow and its protected
`production` environment remain the approval boundary. A develop push is not a
production publication. Manifest v2 binds the GitHub run/attempt ID, exact API and
Worker digests, source revision, installed configuration fingerprint, full EF
migration catalog and an explicit `rollbackAllowed` declaration. The API image
contains the same migration catalog.

Do **not** mark rollback compatible merely because no migration was added. The
previous binaries must also understand any data, files and protected values the
candidate can write. The default is false. An unchanged catalog plus this
declaration is necessary for automatic rollback. It does not prove semantic
compatibility; that remains part of review.

Automatic rollback changes application images only. It never runs a down
migration, restores a database snapshot, removes an image or volume, rotates a
secret or deletes Data Protection keys. Even a failed migration that appears to
have rolled back its transaction requires an operator: the orchestrator does not
infer that the bundle had no other effects.

## One-time upgrade of the existing MK-812 installation

1. Check that the last remote backup and restoration exercise are healthy. Retain
   the exact current image digests and revision, privately. This tool upgrades an
   existing running installation; it is not an empty-VPS bootstrap command.
2. Review and copy the complete MK-820 bundle. Stop the deployment, frontend and
   monitor timers; wait for active jobs to finish. The production installer also
   refuses an active monitor and serializes against backups. It leaves publication
   timers stopped and does not replace credentials or backup units.
3. Install the reviewed production bundle using `sudo bash deployments/production/install.sh`.
4. Separately install the same bundle's reviewed backup code with
   `sudo bash deployments/backup/install.sh`. This preserves the existing backup
   secrets, schedules and locks; do not run credential provisioning again.
   MK-820 preflight refuses backup code whose configuration inventory is older
   than the installed deployment bundle.
5. Create a dedicated ordinary member through normal registration and email
   confirmation, with an operator-controlled mailbox. Do not use an administrator,
   an existing personal account or an SQL insert. This is a one-time human step;
   automatic smoke tests never register an account or send a message.
6. Save its password and immutable member ID in Bitwarden, separately from Restic.
   Run `sudo monkado-deploy provision-smoke` in SSH. All inputs are masked; they
   are never command arguments. The tool verifies the actual account and its exact
   `Member` role, logs out, then writes
   `/etc/monkado/deployment-smoke.json` as root:root 0600. It refuses replacement
   of an existing file. Never paste this file into chat or diagnostics.
7. Generate a migration catalog from a trusted checkout of the **currently
   deployed revision**, not from the MK-820 candidate. Use MK-820's
   `release_catalog.py` against that checkout's
   `src/Infrastructure.Persistence.PostgreSql/Migrations` directory. Review its
   provenance, transfer it as `/etc/monkado/current-migration-catalog.json`,
   root:root 0600. The legacy image cannot supply this new embedded metadata.
8. Run `sudo monkado-deploy adopt /etc/monkado/current-migration-catalog.json`.
   Adoption checks running image IDs, OCI revision labels, the entire database
   migration history, fresh API/Worker telemetry, API and frontend HTTPS, and
   authenticated CRUD/ETag/cleanup. It does not replace an application or migrate.
   A present legacy in-progress marker blocks adoption; resolve that older
   deployment first. Do not remove the marker to bypass the guard.
9. Run `sudo monkado-deploy check-config`, inspect `sudo monkado-deploy status`,
   and review the first newly approved publication. Only then explicitly start
   `monkado-deploy.service`. A successful run permits manual re-enabling of the
   existing deploy/monitor/frontend timers according to their runbooks. Backup
   timers and credentials are not changed by MK-820.

Installation/adoption is deliberately not dispatched by GitHub. For a later
reviewed configuration change, stop timers and reinstall the complete bundle
(including backup code when its inventory changes). Run
`sudo monkado-deploy rebaseline` before publication. This explicitly verifies the
current application and smoke tests against the reviewed new configuration, then
rebinds only that healthy current baseline. It refuses unresolved failures,
does not deploy, and does not forget a rejected publication ID.

## Normal execution and durable outcomes

The lock order is backup-coordination then deploy. The MK-813 inherited descriptor
is reused only when it identifies the same file. Recovery and mutating smoke
commands acquire the same locks. No deployment runs over a backup maintenance
marker.

Before stopping services: validate the approved immutable manifest and installed
fingerprint; verify the current applications; check disk space (2 GiB before pull,
1 GiB afterwards), backup-tool compatibility, active resource slice,
Compose syntax, image labels/catalog, infrastructure, dedicated account identity
and any previous smoke journal. The frontend's public revision marker and
Caddy/PostgreSQL identities/restart counters become the durable baseline.

The state is fsynced and atomically renamed **before** every disruptive phase.
`current.env` remains the three-line backup compatibility pointer, derived from
authoritative `status.json`. Success is committed only after technical and
functional checks and a final full database-history comparison.

| Outcome | Result |
|---|---|
| `succeeded` | Candidate verified, pointer committed, marker removed |
| `rejected` | Preflight failed, no maintenance started, publication latched |
| `rolledBack` | Previous application verified, candidate remains failed/rejected |
| `recoveryRequired` or interrupted nonterminal phase | No unattended retry; marker retained |
| `acknowledged` | Operator verified the recovered current release; rejected ID retained |

A repeated successful or rejected publication is not redeployed. A new attempt
needs a new manually approved workflow run/attempt ID. Fixing a secret does not
silently retry the same publication and repeatedly lock the smoke account.

The oneshot service has a 20-minute overall cap. Readiness requires three healthy
samples, spaced five seconds apart, within a bounded observation window.
Individual HTTP calls have a ten-second timeout and each functional journey a
120-second deadline. Termination requests one controlled recovery; a second
termination or power loss leaves durable intent for the operator. There is no
promise of zero downtime on the single small VPS.

## What the smoke tests prove

The HTTPS socket is pinned to loopback while validating the actual hostname and
certificate chain; no redirects are followed. HTTP must redirect to the precise
HTTPS API hostname. Health content, OpenAPI routes, antiforgery token, exact CORS
allow/deny, restrictive security headers, sensitive no-store responses and cookie
attributes are checked. The frontend marker must remain unchanged.

Running API and Worker must use the expected actual image IDs, have no OOM/restart,
and publish fresh private telemetry from their new process. Caddy and PostgreSQL
are checked against their pre-maintenance identity and restart baseline, rather
than incorrectly rejecting a harmless historical restart forever.

The dedicated member journey is CSRF → login → current identity/role → CSRF →
create/read list → create/read/update/read wish with ETag → delete wish/list →
logout. It validates statuses, bodies, Location and ETag, not only HTTP 200.
It never uses Google, Gmail, URL imports, sharing proofs, uploads or exports.

A root-only journal records synthetic ownership markers **before** POSTs. Lost
responses do not trigger repeated POSTs. Cleanup searches exact unique markers
and refuses foreign/changed/ambiguous contents. It never deletes arbitrary member
data or cascades a list with unexpected contents. An unresolved creation or
cleanup keeps the journal and fails the deployment. Do not remove that journal
to obtain a green result; reconcile through the verified dedicated account.

## Operator recovery

Start with these bounded, non-sensitive outputs:

```sh
sudo monkado-deploy status
sudo systemctl show monkado-deploy.service -p Result -p ExecMainStatus
sudo python3 /opt/monkado/src/Operations.Monitoring/monitor_cli.py status
```

Do not run unrestricted `docker inspect`, print dotenv files or copy raw provider
errors. Review the private state locally if deeper evidence is necessary.

Use the **exact candidate publication ID** shown by status:

- `sudo monkado-deploy recover rollback ID`: only schema-unchanged compatible
  releases with exact previous history; no migration replay. It verifies the
  recovered previous version and leaves the candidate failed.
- `sudo monkado-deploy recover accept ID`: after manually resolving an interrupted
  migration/start, verify the actual candidate, complete history and both smokes
  before committing. It does not run migrations or start containers for you.
- `sudo monkado-deploy recover acknowledge ID`: after a verified rollback or
  preflight rejection, recheck the active release and acknowledge the incident.
  The candidate ID stays rejected; this cannot retry it.

The migration-changing path always requires investigation and explicit approval
for any manual database recovery. Restoration is a separate MK-813 procedure.
`smoke-readonly` never mutates business data; `smoke-write` performs the dedicated
CRUD journey and cleanup. Both are explicitly invoked local commands.

MK-815 reports `deployment.failed`, `deployment.recoveryRequired`,
`deployment.rollbackFailed` and missing evidence. A successful rollback does not
send a false publication-success signal. An acknowledged failure may retain the
old systemd failure result until the next safe timer check; the durable rejected
ID prevents that check from restarting the failed candidate.

## Backup and secret handling

Capture schema v3 includes all new reviewed deployment modules plus optional
private smoke credentials and durable deployment status, **only inside the
encrypted backup**. Restore verification still accepts v1 and v2 snapshots.
Never include the Restic decryption password in its own archive. Gmail/Drive
credentials, Data Protection keys, backup locks and timer schedules are untouched.

After a full restore, do not blindly re-enable publication using the restored
status: container IDs, process snapshots and infrastructure baseline belong to the
lost VPS. Recover the application and revoke old sessions following MK-813, then
perform an explicitly reviewed rebaseline. Reconcile the dedicated account as
well. Changing its password requires a manual, approved handoff; no command here
rotates it automatically.

## Reproducible local validation

Build API and Worker targets from the tested revision with their OCI revision
label, tagged `mon-kado-api:mk820` and `mon-kado-worker:mk820`. Build the test
toolbox:

```sh
docker build -f tests/Operations.DeploymentTests/Dockerfile -t monkado-deployment-tests tests/Operations.DeploymentTests
MK820_DOCKER_CAMPAIGN=1 python3 tests/Operations.DeploymentTests/run_docker_campaign.py
```

The explicit opt-in creates UUID-named labelled networks/volumes and removes only
its own resources. The root test container uses the local Docker socket (therefore
root-equivalent access); run only reviewed test code on a development machine.
Application networks are internal, there are no published ports, TLS uses a
trusted local CA, all credentials/data are synthetic, and the Worker uses Local
with its email provider Disabled. This is a deployment exercise, not a production
email check, Internet/performance qualification or 768 MiB capacity benchmark.

The test adapts systemd and image distribution for local Docker but uses the real
deployment engine, Docker runtime, API, Worker, Caddy, PostgreSQL and smoke code.
It verifies A→B, an intentionally broken candidate→B, rejected-publication latch
and a failed migration with no automatic rollback. Its allowlisted JSONL report
is under `TestResults/mk820-docker-campaign.jsonl`; no response body, cookie,
password or token is exported. Unit tests separately inject lock contention,
power-loss transitions, lost responses, invalid credentials, corrupted state,
schema divergence, resource failures and cleanup ambiguity.
