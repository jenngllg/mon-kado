# Native Go migration qualification

This report records local qualification of the backend migrated from consolidated
develop `89ffcc08a984e8333a90492af7491676bcaea3fd`. It covers all 99 HTTP operations
in the source contract and the concurrent frontend contracts for wish variants,
immutable member tags and reservation history. Route presence alone does not
establish behavioral parity; the component suites below exercise persistence,
authorization, JSON contracts and failures.

## Local evidence on 2026-10-12

At `fd30129`, the complete Windows native PostgreSQL integration suite, `go vet` and race
detector pass through `scripts/Test-Native.ps1 -Race -Coverage`. Backend statement
coverage is **84.62% (4946/5845)**; the autonomous Gmail module is **87.50% (70/80)**.
Both enforce the documented 80% minimum without executable-code exclusions.
These are statement measurements, not branch coverage or a guarantee of no bugs.
Sonar's separate new-code line coverage is 80.4% at `fd30129`. That revision also
passes the complete Linux native integration/race gate at 84.65% (4948/5845)
backend statements and 87.50% for Gmail. The subsequent Sonar security review is
recorded in [the local transport review](native-transport-security-review.md).

The native Linux process suite passes all four process tests, including a full
public HTTP journey through API, worker, local mailbox and export ZIP; invalid
configuration, unavailable database, occupied listener and graceful SIGTERM.
Tests synchronize on actual process/job events. They create a fresh private
Unix-socket PostgreSQL cluster and run the unprivileged Go executables.

## Behavioral coverage

| Journey or invariant | Principal suites under `internal/httpapi` |
| --- | --- |
| Registration, confirmation, password/account lifecycle, sessions, revocation and privacy | `accounts_integration_test.go`, `api_integration_test.go`, `cancellation_integration_test.go` |
| Google browser flow, PKCE/state, provider errors and cancellation | `google_browser_integration_test.go`, `google_integration_test.go` |
| TOTP enrollment, challenges, recovery and reauthentication | `two_factor_integration_test.go`, `two_factor_management_integration_test.go` |
| List creation/update/delete, pagination, archive, favorites, preferences and visibility | `wishlist_collection_integration_test.go`, `preferences_integration_test.go`, `wish_updates_integration_test.go` |
| Wishes, ordering, independent copies, optional variants and immutable tags | `order_integration_test.go`, `copies_integration_test.go`, `copy_failures_integration_test.go`, `wish_variants_integration_test.go`, `member_tags_integration_test.go` |
| Sharing, participants, subscriptions, private resources and previews | `subscriptions_integration_test.go`, `previews_integration_test.go`, `public_profiles_integration_test.go`, `api_integration_test.go` |
| Reservations, capacity races, owner reservations and retained history/media access | `reservation_reads_integration_test.go`, `owned_reservations_integration_test.go`, `reservation_history_media_integration_test.go`, `history_context_integration_test.go` |
| Administration, live role/session/2FA authorization, atomic audit and moderation | `administration_integration_test.go`, `administrative_exports_integration_test.go` |
| Image processing, storage, admission, validation and privacy | `images_integration_test.go`, `image_urls_test.go`, `admission_integration_test.go`, `export_images_integration_test.go`; `internal/images` |
| Merchant extraction, variant selection and bounded provider failure | `import_integration_test.go`; `internal/importer` |
| Private export snapshot, leases, expiry, retry deadlines and ZIP filtering | `exports_integration_test.go`, `export_failures_integration_test.go`, `export_subscriptions_integration_test.go` |
| Outbox attempts, worker state, protected-data purge and notifications | `worker_integration_test.go`, `outbox_integration_test.go`, `notification_integration_test.go`; `internal/worker`, `internal/providers` |
| CSRF, cookies, proxy trust, limits, error schemas, malformed bodies and OpenAPI | `cookie_policy_test.go`, `security_test.go`, `perimeter_test.go`, `body_boundary_test.go`, `failure_boundary_integration_test.go`, `openapi_test.go` |

Supporting deterministic suites test domain values, aggregated validation paths,
cryptography, structured logging, telemetry, embedded SQL catalogs and PostgreSQL
constraints. Public contracts verify business persistence; direct SQL assertions
are limited to technical invariants that the public API cannot expose.

The final parity check found and corrected blank anonymous participant names.
`guest_join_integration_test.go` verifies missing/null/blank/invalid guest names,
eighty Unicode scalars after trimming, persisted guest identity, and identified
members ignoring guest-only details. The optional input and actual response
headers are explicitly asserted in OpenAPI.

## Native operations

`scripts/Test-NativeOperations.sh` passes the real HTTPS, frontend activation and
interruption/recovery, PostgreSQL password rotation, installer preservation and
monitoring tests. Caddy verifies production TLS/SNI, cookie flags, CSRF, CORS,
private caching and exact CRUD cleanup. Each HTTPS/installer fixture has an
anonymous loopback-only namespace; its CA is never installed in the host store.

The bounded monitor exercise completes sixteen virtual-clock cycles with a
128 MiB, zero-swap, 10%-CPU unit, detects the incident once and retains private
bounded state. A separate real 32 MiB OOM is detected through the native service
collector. No real notification is sent.

The full deployment campaign at `72f958c` passes in fixture
`mk816-8ffa94ba602c`: A-to-B process replacement, a real candidate API bind failure
with verified rollback, rejected-publication refusal, and a real SQL failure
retaining recovery intent. Counts, image bytes and cryptographic settings survive;
no database rollback is performed. The deliberately failing SQL exists only in a
private source copy. See [fixture instructions](../../tests/Native.OperationsTests/README.md).

Native encrypted backup capture/restore also has real local Restic/PostgreSQL
qualification and installer isolation; remote Drive operations remain disabled.

## Scope and remaining review

All six long performance profiles pass, totaling 48,600 measured business
operations, without unexpected errors, measured 429 responses, OOM or restart.
They include all eighteen image cases, three fresh ZIP exports, quota enforcement/
recovery and competing ETag/reservation writes. Final stress reaches each
5/10/20/5 requests-per-second target and has no over-reserved wish. Results and
source/binary provenance are recorded in
[the native performance evidence](../performance/go-migration-native-2026-10-12.md).
Performance fixtures share
a one-CPU, 768 MiB, zero-swap budget and keep authentication and business quotas
enabled. Local performance does not qualify production hardware or deployment/
backup overlap.

External providers are deterministic fakes or simulated transports in the quality
gate. This verifies the local contracts and failure handling, not live Google,
Gmail, merchant sites or Drive credentials. The frontend prototype in the user's
checkout is preserved; this backend qualification does not claim a full visual
UI acceptance test of a separately developed frontend.

Sonar's local HTTP cookie exception and OAuth loopback callback now have
[recorded contextual reviews](native-transport-security-review.md); the complete
quality gate passes. The temporary manual review mechanism has been removed.
Neither analysis rules nor thresholds are disabled. CodeQL alert
[34](https://github.com/jenngllg/mon-kado/security/code-scanning/34) was explicitly
reviewed as **used in tests**: freshly generated synthetic fixture passwords in
0600 files, no external route, no production credentials and sanitized reports
only. The CodeQL check is green after that recorded review.
