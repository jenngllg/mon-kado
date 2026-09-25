# MK-820 local validation — 2026-09-25

Implementation tested: `803eb92fe5aedadba7ab1086a5258841adcfc429`.
The subsequent merge of develop integrates MK-816's already-approved CI/coverage
jobs; it does not change the C# application or the deployment engine. The PR CI
also repeats the Docker campaign against its exact checked-out revision.

Environment: Windows development workstation, Linux Docker Desktop, PostgreSQL
18.6, Caddy 2.11.4, Python 3.13 and .NET SDK 10.0.401. This is not a VPS capacity
or production-network qualification. Worker email is explicitly Disabled in
Local; API remains Production. Synthetic credentials/data only, internal Docker
networks, no published application ports, and a trusted local TLS CA.

## Real Docker campaign

Command: `MK820_DOCKER_CAMPAIGN=1 python3 tests/Operations.DeploymentTests/run_docker_campaign.py`
(the equivalent PowerShell environment assignment was used locally).

Base API image: `sha256:818a2bf3b94877edf7c5c48453206e517784c10c04107e142dc9ecdab81b7c61`.
Base Worker image: `sha256:4a05143a0866d0287d4b7efb4f909b54f6f6994b9c929c0aab0369906ac5823a`.
Variants are deliberately distinct local images derived from those builds, not
claims that different released business implementations were exercised.

| Check | Observed result |
|---|---|
| HTTPS, redirect, security/CORS and frontend | Passed with real Caddy and certificate verification |
| CSRF/login, exact Member identity, list/wish CRUD, ETag, cleanup/logout | Passed with real API and PostgreSQL |
| A → B, unchanged schema | Passed; full migration history unchanged |
| Intentionally broken candidate C → verified B | Passed; failed publication retained |
| Existing fixture member/list, image-volume bytes, actual Data Protection key files | Preserved across rollout and rollback |
| Retry of the same rejected publication | Refused without another rollout |
| Intentionally failing migration-changing candidate | Recovery required, marker retained, no automatic application rollback |
| Cleanup | Label-scoped test containers/networks/volumes removed; no production resources touched |

Initial exploratory runs exposed two errors in the smoke implementation: the
wish collection was treated as paginated, and the EF history query used the wrong
column casing. Both were corrected against the actual public API/database
contract and the complete campaign rerun successfully. Tests were not retried
unchanged to hide an intermittent failure.

## Deterministic and quality checks

- Deployment tooling: 68 tests, 100% line and branch coverage; final review adds
  regression tests for explicit local Docker socket selection and recovery
  without an approved candidate.
- Release-manifest validation: 10 tests; 100% line and branch coverage (two
  installed-entrypoint tests are separately enabled in a disposable container).
- Backup suite: 88 discovered, 85 executed; 100% line and branch coverage. The
  three explicit integration opt-ins are not silently represented as executed.
- Monitoring suite: 66 discovered, 64 executed; 100% line and branch coverage.
- Production installer preservation sandbox and installed root-entrypoint
  sandbox: two tests each, all passed.
- ShellCheck, actionlint, diff secret scan: passed.
- Complete .NET restore, format verification, Release build and test run: passed.
  Build: zero warnings/errors. No C# production code was modified.
- Complementary Linux filesystem tests (Images, PersonalDataExports,
  Observability) passed. Combined with Windows coverage: **23,698/23,698 lines and
  4,799/4,799 branches**, verified by the existing coverage gate. Platform-specific
  skips were covered by the other OS, not excluded from the gate.

## Review and operational limitations

Review covered approval binding, wrong image/catalog/history, interruption before
and after durable commits, failed rollback, rejected preflights, old snapshots,
private files, account privilege checks, lost HTTP responses and scoped cleanup.
Corrections include persisting the frontend/infrastructure baseline for recovery,
comparing historical infrastructure restart counters rather than requiring zero,
blocking repeated preflight account attempts, bounded telemetry/body reads, and
an explicit healthy rebaseline after a reviewed configuration reinstall.
The final review also pins the Docker CLI to the local Unix socket: clearing
environment overrides alone does not exclude an operator's saved Docker context.
Sonar findings were corrected without suppressions, including an explicit null
guard before operator recovery and a smaller response-header validation method.
ASCII-only catalog/publication validation remains enforced and tested.
Malformed JSON contracts also become sanitized smoke failures eligible for the
same guarded rollback, rather than escaping the state machine as Python errors.
An existing monitor OOM integration test failed intermittently in CI. Its test
setup now waits for the exact container's Docker OOM event and stopped state
before collecting observations; the real OOM assertion remains mandatory, with
no retry, polling loop or arbitrary sleep.

No VPS installation, smoke-account creation, production publication, real secret
handoff/rotation, Google/Gmail/Drive access, merge, or paid option was performed.
These local results do not replace the runbook's manual production approval and
initial adoption checks. Remote PR checks are the authority for the final merged
candidate and must be green before acceptance.
