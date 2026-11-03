# Online notification expansion

This release adds in-app notifications through the normal protected main
publications. Jenn explicitly requested the complete release, including preparation
of its online migration, on 3 November 2026. It preserves the current production
API, PostgreSQL data, worker configuration, encryption keys and frontend contract.

## Reviewed boundary

Only the exact catalog transition pinned in `coordinated_cli.py` is admitted:

- Previous catalog: `958e79ccf99e58e9a1a7c4818b8dc9e321e18d3ae1d51adc22789e0e0f862467`.
- Expanded catalog: `0a924f9b48095ff150e2d7517fff9fb1980fb4cec34550097184f90d044a4905`.
- Appended SQL: `027_member_notifications.sql`, SHA-256
  `6f475c239fa94045769ee85712c2e7c9a226ad4959c8b6273d864d9bea1ad2b8`.

All 26 historical SQL files retain their exact bytes. The expansion creates an
empty notification table, its indexes and invalidation triggers, and adds a
constant-default boolean to wishlists. PostgreSQL 18 adds that column without
rewriting existing rows. No existing column is removed or renamed, no existing
value is modified, and no notification data is discarded during binary rollback.
The migration and ledger entry share one transaction under the existing migration
advisory lock. Transaction-local lock and statement deadlines are 2s and 30s.
Contention aborts the entire expansion, including its earlier DDL, while the old
API remains available. No migration retry is hidden in this procedure.

## Qualification

The PostgreSQL integration scenario holds a real legacy list read across the
attempted expansion, verifies lock-timeout rollback of schema and ledger, confirms
the original row remains readable, then releases the reader and verifies one
successful, idempotent expansion. Notification integration tests cover live badge
events, authorization, surprise-mode confidentiality, persistence, guest identities,
reminders and exports. CI additionally compiles and runs the previously published
API's HTTP integration suite at `78b034a4bfead8a8421709ae1e903b1fa638387d`
against the expanded database.

Operational scenarios cover exact catalog admission, refusal of changed SQL,
backup ordering, committed-SQL recovery after lost acknowledgement, binary-only
rollback and refusal of divergent database history. Ordinary online deployments
retain their unchanged-catalog policy. An arbitrary later SQL migration is refused.

## Installation and rollout

1. Qualify and merge the backend and paired frontend revisions through their
   protected main workflows. Keep all checks and publication proofs intact.
2. Publish the matching coordinator inventory using the existing main-only tool
   publication. Install it with the documented `upgrade-online SOURCE` procedure
   from [Production from main](main-vps.md). Its new inventory includes the
   deployment engine's policy hooks, and still validates the older installed
   inventory. This operational installation requires fresh verified encrypted
   off-VPS captures before and after installation. Do not edit an installed
   fingerprint or replace unreviewed files manually.
3. An application request made before the reviewed tools are installed must fail
   before any migration or switch. Once installation is verified, request the
   same checked main publications through the normal workflow. This is recovery
   after a completed prerequisite, not a retry of an uncertain database mutation.
4. The coordinator pins the complete pair and verifies a new coherent PostgreSQL
   and durable-file snapshot, its encrypted off-VPS transfer and integrity before
   applying SQL. That operation's snapshot is shared by both component switches.
5. Keep the old API and Caddy serving during SQL. Start and probe the candidate API
   on loopback before routing requests to it; drain the old process and switch the
   frontend atomically. Notification streams end within 90 seconds so later API
   releases can drain, then reconnect to the selected API without losing badge state.
6. Verify the durable pair receipt, matching snapshot references, readiness and
   liveness, worker state, protected notification endpoints and the served frontend
   revision. Retain the snapshot reference in the delivery report.

## Recovery

Use `online-recover` for the original pinned intent, subject to its existing
one-hour snapshot freshness check. A committed migration is recognized by its
complete exact history and is never replayed after lost acknowledgement. If the
candidate API fails before successful activation, the specialized engine can
verify and restart the previous binaries against either the unchanged catalog or
this exact additive expansion. The notification table, column, ledger and read
state stay in place. Unexpected or partial history requires explicit investigation;
never drop data, rewrite an immutable manifest, delete intent to force a new release
or substitute an unrelated snapshot. Retain failure intent and follow the normal
verified recovery procedure before another publication.
