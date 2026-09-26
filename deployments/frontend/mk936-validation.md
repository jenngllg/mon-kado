# MK-936 local validation — 2026-09-26

Implementation tested: `12d01ce8a62f6274636c5ac8b48a3f5134bb2deb`.
This report is added separately; CI repeats the checks against its exact checkout.
No C# application, frontend UI, business contract, migration, quota or production
secret is changed. The full existing multi-platform quality gate remains mandatory.

## Executed HTTPS campaign

Environment: Windows workstation, Linux Docker Desktop, Python 3.13, curl and
Caddy 2.11.4. Disposable labelled volumes and an internal Docker network, no
published ports. The generator shares Caddy's network namespace: the production
probe's loopback hostname overrides cannot contact the real domain. Certificates
are verified using this run's local CA; verification is never disabled.

```sh
docker build -f tests/Deployment.FrontendTests/Dockerfile -t monkado-frontend-tests tests/Deployment.FrontendTests
MK936_DOCKER_CAMPAIGN=1 python3 tests/Deployment.FrontendTests/test_frontend_campaign.py
```

The equivalent PowerShell environment assignment was used locally. The final
campaign completed successfully in 6.165 seconds; this is test execution time,
not a production recovery-time guarantee. The CI artifact is the allowlisted
`TestResults/mk936-frontend-campaign.json` (schema 1).

| Scenario | Observed result |
|---|---|
| A → B with real HTTPS, content/hash, headers, marker and legal pages | Passed |
| Candidate with a missing JavaScript entrypoint | Rejected; B restored and freshly verified |
| Timer attempt with historical `--retry` after failure | Remained paused |
| Explicit rollback to A | Verified; automatic publication remained paused |
| Resume for B | Authorized without switching; subsequent explicit deployment succeeded |
| Child process exit after D activation and HTTPS check, before final commit | Durable journal recovered to verified B; no chained publication |
| Actual corrupt HTTPS asset bytes during candidate and fallback checks | Recovery required; journal retained; timer refused retries |
| Explicit recovery after fixture repair | Verified B restored; journal removed, suspension retained |
| Backend revision file | Unchanged |

Artifacts are synthetic static pages with Google disabled. Readiness is a local
Caddy fixture returning the API's `text/plain` `Healthy` contract, **not** a real
API/PostgreSQL dependency test. This campaign qualifies frontend release mechanics;
MK-820's separate real backend/database campaign and browser acceptance still apply.
No Gmail, Google, Drive, member data, production DNS, or VPS was used. Test-owned
containers, networks and volumes were removed. The reusable local toolbox image remains.

The existing real-Caddy routing suite also passed (one integration test): SPA routes,
static assets, legal documents while the API is down, redirects, and private/missing
paths. It uses loopback-only published ports and synthetic responses.

## Deterministic checks and review fixes

- Frontend: 50 discovered, 48 deterministic tests executed; **100% lines/branches**.
  Both explicit Docker opt-ins were executed separately as described above.
- Monitoring: 68 discovered, 66 executed; **100% lines/branches**. Provider and
  resource-budget opt-ins are not represented as locally executed here.
- Backups: 89 discovered, 86 executed; **100% lines/branches**. No real Drive call.
  Schemas 1–3 restore with their original inventories; new captures use schema 4.
- Release-manifest tooling: 12 discovered, 10 deterministic tests executed;
  **100% lines/branches**. Production installer preservation sandbox: two tests passed.
- Shell syntax, ShellCheck, actionlint, frontend secret scan and `git diff --check`: passed.

Review covered first-publication failure, candidate/fallback verification, unchanged
revisions with corrupted files, archive trust, bounded history/state, lock ordering,
remote manifest drift after resume, interrupted preparation and switching, and
successful state committed before journal removal. Regression fixes:

1. Verify referenced JS/CSS exists rather than accepting a correct HTML hash alone.
2. Permit manual rollback from an unhealthy current site, still verifying the target.
3. Preserve and latch corrupt recovery journals instead of retrying every timer tick.
4. Include failed systemd outcomes in monitoring even when older metadata says healthy.
5. Reload durable recovery state before recording an outer publication failure.

Local test failures during implementation were fixed in code/fixtures, not retried
unchanged: backup schema expectations, monitoring healthy fixtures and one test
indentation error. No coverage exclusions or lowered thresholds were introduced.

## Approval and operational limits

No merge, installer run, production publication, secret rotation, timer change or
paid option was performed. Installation needs its own approval and a matching
backend configuration publication; upgrade the backup inventory tooling together.
Production currently cannot be inferred to run this code from these local results.

Exact backend/configuration compatibility remains fail-closed, including the
fallback. This is not a cross-version compatibility matrix: coordinated upgrades
with incompatible retained releases need a separately reviewed recovery decision.
The system cannot invent a fallback for the first deployment, undo a migration,
or guarantee browser behavior from static HTTP checks. MK-828 legal publication
approval and Google gating remain unchanged; this work does not resolve MK-831's
operator-identity prerequisite or create preproduction.
