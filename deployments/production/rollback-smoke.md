# Native deployment verification and recovery

Manifest schema 3 binds approved API/worker/migration executable bytes, source
revision, publication identity, complete SQL history and installed configuration.
Production publication remains manually reviewed. This runbook does not authorize
a production installation, credential rotation or deployment.

## Readiness and persistence checks

Every disruptive phase is persisted and fsynced before its side effect. The lock
order is backup coordination then deployment; the verified inherited backup lock
can be reused. A backup maintenance marker prevents application deployment.

Before maintenance, verify the current running executable hashes, approved candidate
metadata/catalog, 2 GiB free disk (1 GiB after download), backup inventory, resource
slice, private settings, dedicated member login and any previous cleanup journal.
Keep the frontend marker and PostgreSQL process identity as the durable baseline.
Caddy's unit/boot identity, executable and restart count remain stable across the
planned stop/start; its new process identity is expected.

Technical smoke requires three consecutive healthy samples within 180 seconds.
It verifies HTTPS certificates on loopback with the real hostname, the exact HTTP
redirect, health, OpenAPI, CSRF, CORS allow/deny, security headers, no-store responses
and production cookies. API/worker must run the approved hashes with no OOM or
restart and private telemetry newer than their current process start. Missing,
malformed, public, foreign-owned or stale telemetry fails readiness.

The ordinary dedicated member journey checks login/role, list CRUD, wish CRUD with
ETag, persistence, Location, response schemas, cleanup and logout. It does not send
email or call a real merchant. Lost POST acknowledgements are reconciled through
exact unique synthetic markers; they are not retried. Foreign/changed/ambiguous
content blocks cleanup. The private journal remains until safe reconciliation.

Readiness on a first empty installation is insufficient: explicit adoption must
complete the authenticated journey before the baseline and pointer are confirmed.
Subsequent success also requires exact SQL history after the journey.

## Durable outcomes

| Phase | Meaning |
|---|---|
| succeeded | Candidate and full journey verified, baseline committed, marker removed |
| rejected | Preflight failed before maintenance, publication rejected |
| rolledBack | Previous application verified, candidate still rejected |
| recoveryRequired or interrupted phase | Durable uncertainty, no unattended retry |
| acknowledged | Recovered active application verified, rejected ID retained |

A rejected publication is never retried silently. A new attempt needs a new protected
publication ID. Retained binaries are verified before every rollback; there is no
automatic release pruning. Declare rollback compatibility only after reviewing the
previous application's ability to read data/files/protected values written by the
candidate. An unchanged catalog alone is insufficient. A SQL-changing deployment
cannot automatically roll back. No recovery command runs a down migration.

## Operator commands

Use bounded status outputs; never print private EnvironmentFiles, provider errors,
process environments or command lines containing credentials:

```sh
sudo monkado-deploy status
sudo systemctl show monkado-deploy.service -p Result -p ExecMainStatus
sudo python3 /opt/monkado/src/Operations.Monitoring/monitor_cli.py status
```

Use the exact failed publication ID:

- `sudo monkado-deploy recover rollback ID`: restore a verified compatible previous
  executable set only with unchanged exact SQL history. Candidate stays rejected.
- `sudo monkado-deploy recover accept ID`: after manually repairing candidate startup
  or migration, verify its binaries, full history and both smokes before committing.
  It does not start applications or replay SQL.
- `sudo monkado-deploy recover acknowledge ID`: recheck a verified rollback/preflight
  rejection and acknowledge it. The publication remains rejected.

For interrupted first installation, retain its exact approved manifest and use
explicit `adopt` after manually resolving the installation. Do not call normal
recovery over a nonexistent previous baseline. Successful adoption completes both
smokes and clears intent without replaying SQL.

After reviewed configuration changes, stop timers, reinstall the full bundle and
its matching backup code, then run `sudo monkado-deploy rebaseline`. It rechecks the
healthy current binaries, history and smokes, preserves rejected publication IDs,
and refuses unresolved failures. Restoration remains a separate encrypted-backup
procedure; it must be qualified natively before the final migration merge.
