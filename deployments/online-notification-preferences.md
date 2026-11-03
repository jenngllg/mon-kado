# Reviewed online notification preference expansion

Jenn requested publication of all current local changes on 3 November 2026.
This separately reviewed transition retains the deployed 27 migrations byte for
byte and appends only `028_notification_preferences.sql`. The previous catalog
fingerprint is `0a924f9b48095ff150e2d7517fff9fb1980fb4cec34550097184f90d044a4905`;
the expanded catalog is `be71e86a53f80adab945479aa01a3a205a846ca0b76c8da088c8b437a0f64081`.

The migration creates an empty account-owned preference table and supported-kind
constraints. It does not update or backfill existing rows. Missing rows preserve
all-enabled defaults. The table's foreign key follows account erasure. All SQL
and its ledger entry commit transactionally under the standard advisory lock,
with a 2-second lock timeout and a 30-second statement timeout. A contention
failure leaves the current API and Caddy serving the existing schema.

The exact 027 -> 028 pair is added to the existing finite admission list; the
earlier 026 -> 027 exception is retained. Modified SQL, skipped versions,
unreviewed appended migrations, divergent history, missing rollback approval,
unqualified artifacts and failed/offline/unverified backups remain refused.
Every existing API operation and schema is preserved. Owner identities needed
for accurate dashboard birthday grouping use a separate authenticated additive
resource, bounded and filtered by current subscription grants.

Install only the protected, exact-revision CI-qualified coordinator package via
`upgrade-online SOURCE`, with independently verified encrypted off-VPS captures
before and after installation, as described in [the notification rollout](online-notifications.md).
An application request made before this prerequisite completes must fail before
migration or switching. Then request the same checked main publications normally.
The coordinator captures another new coherent, encrypted, remotely verified
snapshot before either application component switches; both share that snapshot.

The old API continues during SQL. Candidate API readiness and real functional
checks precede selector replacement, connection draining and the atomic frontend
switch. Caddy is neither stopped nor reloaded. The worker is updated with the API.
Binary rollback retains the preference table and all saved choices. Previous
binaries do not enforce opt-outs, which is a known rollback behavior limitation;
never drop preference data or replay an uncertain write to compensate.

Qualification includes native PostgreSQL preference/workflow/privacy tests,
recurrent birthday and leap-year scenarios, the previous API's integration suite
on the expanded database, the same lost-acknowledgement/rollback/backup-order
operational scenarios for both exact transitions, and the required native
continuity and encrypted restore quality gates. Record final main revisions,
tool-upgrade receipts and all backup references after the published-site checks.
