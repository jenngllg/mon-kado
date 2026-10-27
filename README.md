# MonKado backend

MonKado runs as native Go executables backed by PostgreSQL. The API, worker, migrations and Gmail OAuth bootstrap tool require neither .NET nor Docker. The migration is based on consolidated `develop` at `89ffcc0`.

## Migration compatibility status

All 138 HTTP operations from consolidated develop are implemented in Go, including archives, favorites, surprise mode, independent wish copies, owner reservations, public member profiles, member search visibility preferences (visible by default), public social previews and revocable subscriptions. The concurrent local frontend additions are preserved: optional wish color/size, immutable server-assigned member tags and reservation history accessibility reasons. Targeted tests verify their JSON, authorization, concurrency and lifecycle contracts. Merchant extraction includes exact variant selection, microdata, Amazon, FNAC and the optional public Sephora catalog. Private operational telemetry and filtered structured logs are implemented in Go. Native release publication, deployment transitions, supervision, encrypted backup/restore and installer isolation have local Linux tests. Real native HTTPS and frontend recovery tests are documented under `tests/Native.OperationsTests`. All six local performance profiles pass 48,600 measured business operations under one CPU, 768 MiB and zero swap. The local transport exceptions have a [recorded security review](docs/testing/native-transport-security-review.md), and the complete protected quality gate passes.

## Native development on Windows

Private groups support member invitations, admission by revocable link/code, private photos and explicit list sharing. Reservations keep global quantity limits across public and group access; durable history never turns a group grant into a public share secret. Wishes expose `isSurprise` and `acceptsSecondHand`, including copies and exports. New accounts are visible in member search by default; existing saved visibility preferences are preserved by the forward migration.

Requirements: PowerShell 7, Go 1.27 and PostgreSQL 18. Scripts find `go`, `initdb`, `pg_ctl` and `psql` on PATH, or use portable distributions under `.local/toolchains/go` and `.local/toolchains/pgsql`.

```powershell
./scripts/Initialize-Native.ps1
./scripts/Start-Native.ps1 api
```

In another terminal:

```powershell
./scripts/Start-Native.ps1 worker
```

Initialization starts an isolated PostgreSQL cluster on `127.0.0.1:55432`, creates `mon_kado_go` and `mon_kado_go_test`, generates local secrets and applies migrations. Subsequent runs reuse the cluster and settings. Runtime files and secrets remain under Git-ignored `.local`. Keep `.local/environment.json` and `.local/postgres-password` private. The API listens on `http://127.0.0.1:7000`.

```powershell
./scripts/Test-Native.ps1
./scripts/Test-Native.ps1 -Race
./scripts/Build-Native.ps1
```

Build produces standalone `api`, `worker`, `migrate` and `gmail-oauth-bootstrap` executables in `.local/bin` with CGO disabled. Running the binaries needs no Go toolchain. Stop the local database cleanly with:

```powershell
& ./.local/toolchains/pgsql/bin/pg_ctl.exe -D .local/postgres-data stop -m fast
```

Race tests require a native C compiler for Go's detector. Set `CC` or put GCC on PATH; Windows scripts also find a portable LLVM MinGW distribution under `.local/toolchains/llvm-mingw-*`. This compiler is only used for tests. `Test-Native.ps1 -Coverage` generates both module profiles, a combined SonarQube report and checks each module against the documented Go coverage minimum below.

## Configuration

Configuration comes from process environment, validated once at startup. Windows scripts also load `.local/environment.json`; process variables take precedence. On Linux use a private environment file or secret manager. Never version credentials.

| Variable | Purpose / default |
| --- | --- |
| `DATABASE_URL` | Required PostgreSQL connection URL; use TLS outside the local loopback cluster |
| `JWT_SIGNING_KEY` | Required Base64 secret, at least 32 decoded bytes |
| `DATA_ENCRYPTION_KEY` | Required Base64 secret, exactly 32 decoded bytes; keep stable for protected data |
| `APP_ENV` | `local` or `production` (HTTPS origins and secure host cookies) |
| `HTTP_ADDRESS` | `:7000`; bind loopback behind a reverse proxy |
| `JWT_ISSUER`, `JWT_AUDIENCE` | `MonKado.Api`, `MonKado.Frontend` |
| `FRONTEND_ORIGIN` | Exact origin, default `http://localhost:5173` |
| `ALLOWED_ORIGINS` | Comma-separated exact origins; HTTPS in production |
| `ALLOWED_HOSTS` | Exact hosts/IPs, default `localhost,127.0.0.1` |
| `TRUSTED_PROXIES` | Explicit proxy CIDRs; empty by default |
| `IMAGE_PATH`, `EXPORT_PATH` | `.local/gift-images`, `.local/personal-data-exports` |
| `EMAIL_PROVIDER` | `Disabled`, `Mailbox` (local files only) or `Gmail`; native initialization selects Mailbox |
| `GMAIL_SENDER_ADDRESS` | Exact sender address, required with Gmail |
| `GMAIL_CLIENT_ID`, `GMAIL_CLIENT_SECRET`, `GMAIL_REFRESH_TOKEN` | Required with Gmail |
| `GOOGLE_ENABLED` | `false` |
| `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` | Required when Google sign-in is enabled |
| `PUBLIC_API_ORIGIN` | Exact HTTPS origin required for Google callbacks |
| `PROVIDER_HTTP_TIMEOUT` | `15s`, positive, at most `1m`, shorter than a job lease |
| `PROVIDER_FAILURE_THRESHOLD`, `PROVIDER_BREAK_DURATION` | `5`, `30s` (Gmail circuit breaker) |
| `WORKER_INTERVAL`, `OUTBOX_MAXIMUM_ATTEMPTS` | `10s`, `10` |
| `WISH_IMPORT_TIMEOUT`, `WISH_IMPORT_MAXIMUM_HTML_BYTES` | `20s`, `4194304` (4 MiB); lower budgets are supported. Explicit older limits remain effective until deliberately updated. Each GET allows at most two transient retries with cancellable 250/500 ms backoffs inside the same preview deadline. Permanent refusals, unsafe destinations and oversized responses are never retried. The serialized preview admission gate is unchanged. |
| `WISH_IMPORT_MAXIMUM_REDIRECTS`, `WISH_IMPORT_PERMIT_LIMIT` | `3`, `10`; preview quota is per member per minute |
| `JOB_BATCH_SIZE`, `EXPORT_BATCH_SIZE` | `100`, `5` |
| `JOB_LEASE_DURATION`, `JOB_RENEWAL_INTERVAL` | `2m`, `30s`; renewal precedes expiry |
| `OUTBOX_FIRST_RETRY_DELAY`, `OUTBOX_MAXIMUM_RETRY_DELAY` | `1m`, `24h` |
| `EXPORT_MAXIMUM_ATTEMPTS`, `EXPORT_TIMEOUT` | `5`, `10m` |
| `EXPORT_LIFETIME`, `EXPORT_MAXIMUM_BYTES` | `24h`, `1073741824` (1 GiB) |
| `PENDING_FILE_GRACE_PERIOD` | `1h`, longer than an export attempt |

Native initialization uses `EMAIL_PROVIDER=Mailbox`: the worker writes private JSON messages under `.local/mailbox`, including the action URL for confirming local accounts. It requires no Gmail credentials or mail server, and is rejected in production. `MAILBOX_PATH` changes this directory. Disabled email keeps outbox messages in PostgreSQL without contacting Gmail; expired protected proofs are still cleared and terminal records are retained for 30 days. Registration still needs email confirmation; enable Gmail to receive links. Integration tests use in-memory providers. Google sign-in and Gmail delivery have independent configurations.

The autonomous Gmail tool remains in `src/Tools.GmailOAuthBootstrap`:

```powershell
go -C src/Tools.GmailOAuthBootstrap run .
```

Set its OAuth desktop client credentials in the environment first. It prints a send-only consent URL, receives a PKCE-protected loopback callback, then displays the refresh token once without storing it. Save that value privately as `GMAIL_REFRESH_TOKEN`.

## Architecture and contracts

`cmd/api`, `cmd/worker` and `cmd/migrate` are the composition roots. `internal` separates HTTP, application transactions, domain contracts, validation, persistence, security, images, merchant imports, providers and background jobs. The Gmail bootstrap tool is an independent standard-library Go module.

Operations from the migration source branch are retained: authentication/refresh, Google linking, administrator two-factor enrollment/recovery, profiles, wishlists, wishes, sharing, guest participation, reservations/history, images, merchant previews, reports/moderation, audits, account erasure, session revocation and personal data exports. The newer develop operations are listed in the compatibility status above.

`/openapi/v1.json` documents the API. `/liveness` checks the process; `/readiness` checks PostgreSQL. API startup never migrates. Run `migrate` before upgrading API and worker together. Embedded SQL migrations use a PostgreSQL advisory lock. The initial baseline retains historical schema identifiers with no EF Core runtime dependency.

Creation returns 201, Location and a complete DTO; updates return 200 with the DTO; deletion returns 204. Concurrency-sensitive changes use strong ETag/If-Match: 428 when absent, 400 when malformed, 412 when stale. Private foreign resources return 404. Errors keep the exact five-field envelope and stable domain codes. Nullable fields are included, enums are camelCase strings, timestamps are UTC and money remains decimal.

Obtain `X-CSRF-TOKEN` from `GET /security/csrf-token` using the same cookie jar and authentication state. Retrieve a fresh anonymous CSRF token before refreshing after access-token expiry. Share secrets stay in frontend URL fragments and use `X-MonKado-Share-Token` for API requests.

Images use a pure Go lossless WebP encoder, preserving normalization, dimensions, orientation, metadata stripping and grant-based access. Encoded bytes differ from the previous lossy encoder. No native image library or CGO dependency is needed.

API and worker share database and private image/export directories. Outbox leases and explicit backoff own every Gmail send attempt; there is no transparent POST retry. Exports stream a consistent snapshot into private ZIPs, exclude credentials/share secrets/other participants' private reservations, and expire automatically. Staged files are reconciled against live references before deletion.

## Tests and quality

```sh
go vet ./...
go test ./...
MONKADO_TEST_DATABASE_URL=... go test -tags=integration ./...
go -C src/Tools.GmailOAuthBootstrap test ./...
```

Integration tests use native PostgreSQL with a database name ending in `_test`. The shared fixture migrates and resets it; never point it at a database with data to preserve. Providers are replaced by deterministic fakes, with no external network calls. Windows scripts configure the isolated test database automatically.

The [local migration qualification report](docs/testing/go-migration-local.md) records the tested journeys, operational failure scenarios, measured coverage and remaining qualification limits.

On native Linux with PostgreSQL 18 and Go installed, `sudo env "PATH=$PATH" bash tests/Native.ProcessTests/run.sh coverage.native.out` compiles instrumented API, worker and migrator binaries, creates a fresh private Unix-socket database and exercises registration, local worker email delivery, confirmation, login, lists, wishes, favorites, copies, sharing, subscriptions, reservations, archiving, exports, privacy denials, logout and graceful SIGTERM shutdown. It contacts no external provider and changes no existing database or installed application service. Startup checks also reject invalid configuration while keeping public binary metadata available. CI merges the real process coverage with both Go module reports; instrumentation is confined to fixture builds, with no executable-code exclusions.

CI installs PostgreSQL directly on Linux, runs integration/race tests, checks formatting and modules, scans Git history with native Gitleaks, builds with CGO disabled and runs [govulncheck](https://go.dev/doc/security/vuln/). It uses no Docker services. No retries hide failing tests. Coverage selects the module import path explicitly so portable toolchains under `.local` are not included; repeated profile blocks are merged before evaluating the gate.

### Coverage exception for the Go migration

The initial 100% target was evaluated against complete profiles. The automated minimum is now **80% of statements in each module**, with no executable code excluded from those profiles. The 100% objective remains for reachable behavior; Go's statement metric does not establish branch coverage. This is the concrete exception allowed by the repository instructions, rather than a claim that the previous target was achieved.

Several defensive returns cannot be reached through their public contracts: `Store.Migrate` reads an immutable embedded directory and filenames obtained from that same directory; Google flow serialization marshals structs containing only supported JSON types; `HashPassword` passes fixed valid parameters to PBKDF2; AES-GCM receives the AES block created immediately beforehand. Exercising those returns would require substituting standard-library internals or changing production invariants solely for coverage. They remain visible and count against the minimum. Other uncovered dependency-failure paths remain measured as well, so this exception does not assert that every remaining gap is unreachable.

Native executable startup and the local mailbox workflow are additionally verified through real binaries. PostgreSQL tests cover a fresh schema, migration idempotence, rollback after a conflicting schema, request transactions, access isolation, stale versions and concurrent reservations. Provider and worker tests cover explicit retry deadlines, terminal failures, protected-data cleanup and cancellation without contacting Google or Gmail. Future changes must keep both module gates passing and add explicit scenarios for their affected behavior.

See [native deployment](deployments/README.md). Existing audit documents and prototypes are preserved. Old backend C# build outputs and unused project directories have been removed. Legacy .NET disk-management helpers and MSBuild guards are removed as requested. Historical result/archive directories can contain source checkouts and must not be deleted wholesale; `.local` contains the active native database, secrets, files and toolchains.

## Group audiences and admission previews

Public wishlists are visible in all current and future groups of their owner by default. The owner can save per-group exclusions atomically through the versioned group-visibility resource; private lists still require explicit grants. Migration 018 adds audience policies and exclusions without changing existing migration sources or deleting member data. Reads, image access and reservation history use the same effective grants.

Owners can read their lists through a group without joining as a participant or reserving. Surprise mode hides every reservation-derived quantity before filtering. Active admission links advertise only group identity and its current photo in the invitation page and social previews; revocation removes that public preview. Admission codes remain in frontend fragments and the dedicated API header.

Group-derived follows retain their original group authorization, without creating public sharing links or participant records. Migration 019 adds this source to subscriptions and revokes private follows when access is withdrawn. Public subscriptions retain their existing capability binding. The complete Linux race suite has an explicit 15-minute budget because the expanded native HTTP integration suite can exceed Go's implicit 10-minute package limit; assertions, race checks and coverage requirements remain unchanged, without retries.
