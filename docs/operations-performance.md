# Local performance qualification (MK-816)

This harness sends traffic only to a disposable Docker deployment. It cannot
qualify production hardware, Internet latency, frontend rendering, Gmail,
Google, backup overlap or deployment overlap. It never connects to the VPS.
Use local k6, not Grafana Cloud; no account, paid runner or paid service is needed.

## Fixed acceptance contract

The nominal run has three minutes of excluded warmup followed by fifteen minutes
at **10 business HTTP requests/second**: 30% member lists, 20% wishes, 30% shared
lists, 10% wish updates and 10% reservation/cancellation. These are requests, not
multi-request journeys. Authentication, CSRF, fixture preparation and refresh
requests are additional traffic and reported separately.

Every read family must have p95 <500 ms; every ordinary write family p95 <1000 ms.
Unexpected errors must be <1%, with zero nominal 429s, zero dropped iterations,
zero OOM kills and zero restarts. Missing samples are incomplete, not passing.
Percentiles use the union of successful request samples across all ten generators,
never the average of generator percentiles. k6 thresholds and the merged evaluator
both return a nonzero exit status on failure.

There are ten actual Docker source addresses. Caddy derives the forwarded address
from the network connection; the generators never forge forwarding headers.
General (300/minute/IP) and business quotas stay enabled. In particular, shared
list traffic is kept below its tighter 60/minute/IP quota in nominal traffic.

## Prerequisites and execution

Use Python 3.13+, a **local Linux Docker Engine with cgroup v2**, Docker Compose,
and enough free host memory for the generators in addition to the system under
test. The runner refuses remote Docker endpoints and Docker environment overrides.
Run from the exact reviewed checkout; the orchestrator builds Release images before each campaign (Docker caches unchanged layers):

```powershell
docker pull grafana/k6:2.3.0@sha256:9c2dee7f8ed74d317e4027c06a10f169b625638189de8d4555d0b3486a5aeb34
python src/Operations.Performance/performance_cli.py run --profile smoke
python src/Operations.Performance/performance_cli.py run --profile nominal
```

The application, database and Caddy share a dedicated **768 MiB**, **zero swap**,
**one logical CPU** cgroup. Generators and the observer are outside that budget.
The small cgroup helper temporarily needs privileged local Docker access to create
or remove precisely this run's group; it never changes the parent controllers or
other groups. A failure to apply or verify limits prevents VPS-profile qualification.
Docker container-level memory displays do not necessarily show inherited parent
limits: the report reads the parent cgroup's effective files directly.

The API runs in Production, with Release binaries, original pools/GC settings and
JSON logging. The Worker uses **Local** with its existing Disabled email provider.
This explicit difference prevents external mail and means email throughput is not
qualified. Google is disabled. All runtime networks are internal; no host ports are
published. HTTPS verifies the run-specific Caddy CA. No production credentials,
real data, backup repository or production volumes are mounted.

Preparation creates a fresh marked database with 100 members, 500 lists and 10,000
wishes, then uses real HTTP login, CSRF, sharing and upload contracts to initialize
100 gift images, 100 profile images, participations and reservations. Passwords,
JWT signing material and share proofs are disposable and stay in private local
files/memory. Login and refresh behavior is not bypassed. Long scenarios renew
tokens with the original application lifetime.

## Additional profiles

Each profile creates a new isolated dataset. Do not execute multiple performance
campaigns simultaneously when comparing measurements.

| Profile | Behavior |
|---|---|
| smoke | 30 seconds of the nominal request mix; functional CI check, not hardware qualification |
| nominal | 3-minute warmup and 15-minute measured nominal load |
| images | Read traffic plus gift/profile uploads: 2 MP, near 10 MiB, 40 MP; one then two concurrent uploads per case |
| exports | Read traffic for 25 minutes plus one export followed by two fresh exports; 10-minute deadline each; ZIP CRC, image presence and member identity checked |
| quotas | Read traffic plus an independent single-IP limiter probe, Retry-After/no-store checks and recovery |
| concurrency | Read traffic plus competing ETag updates and full-capacity reservations; persisted update and exclusive success checked |
| stress | After warmup, 5/10/20/5 requests per second for five minutes each; 20 is diagnostic, not the nominal target |

Supplemental checks use a small standard-library HTTP client alongside k6 traffic
so ZIP files can be fully validated without a remote JS dependency. They use three
reserved fixture members that are not used for background writes.

Stop conditions: OOM, restart, thirty seconds of continued service unavailability,
more than 5% unexpected errors in a full rolling minute, or a bounded campaign
deadline. No automatic retry, resizing or threshold relaxation follows failure.
Heavy-image latency is diagnostic; ordinary read latency and process stability
remain visible during those checks. Exports enqueue only synthetic notifications,
which the disabled email provider does not send.

## Evidence and cleanup

Results are under ignored `TestResults/performance/<run-id>/`.
`report.json` has schemaVersion 1, merged percentiles, counts, resource samples,
limits, dataset invariants and passed/failed/incomplete verdict.
`report.md` is the human summary. Image IDs, revision, local host facts and harness
SHA-256 fingerprints distinguish runs. Harness files are frozen for each new run.

Only sanitized metrics and reports may be attached to a MR. **Never upload the
entire result directory**: Compose configuration and fixture credentials are private.
Do not enable k6 HTTP debug logs. Public metric tags are fixed family/phase labels;
no headers, cookies, body content or token-bearing URLs are retained in reports.

After reviewing the evidence, remove this run's disposable containers, networks
and volumes by its printed identifier:

```powershell
python src/Operations.Performance/performance_cli.py cleanup --run-id mk816-012345abcdef
```

Cleanup checks ownership labels and does not use Docker prune or target other
projects. It deletes only test data in this run's Docker resources; reports remain
locally. A failed or interrupted run must not be reported as successful.

If a target fails, retain the evidence and describe the bottleneck. Architecture,
business migrations, production quota changes or VPS upgrades require a separate
decision. No merge, deployment or secret rotation is performed by this harness.
