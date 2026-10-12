# Local performance qualification (MK-816)

## Latest release-quality evidence

Read the [MK-938 campaign review](performance/mk938-2026-09-26.md) before
interpreting older passing reports. It preserves the historical concurrent
40 MP PNG OOM and Docker clock discrepancy. The subsequent approved admission
campaign passes all 18 image cases without OOM or restart under the unchanged
768 MiB budget, but reaches the memory limit and still fails the observed-rate
gate. It does not establish spare memory capacity or production qualification.
Complete counts alone do not qualify the requested throughput;
every measured performance stage must meet its observed-rate requirement.
The short CI smoke is explicitly functional, not a shared-runner performance
reference. Do not rescale historical timestamps or relax thresholds to pass.

The stress executor includes a one-second zero-arrival tail after its four
measured 300-second stages, avoiding a race at the final scheduled arrival.
This tail adds no business requests or extra measured stage; all 12,000
operations and the existing in-flight grace period remain required.

This harness sends traffic only to disposable native Linux processes in an
isolated network namespace. It cannot
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

There are ten actual loopback source addresses. Caddy derives the forwarded address
from the network connection; the generators never forge forwarding headers.
General (300/minute/IP) and business quotas stay enabled. In particular, shared
list traffic is kept below its tighter 60/minute/IP quota in nominal traffic.

## Prerequisites and execution

Use local Linux with systemd, cgroup v2 CPU/memory controllers, Python 3.12+,
Go matching `go.mod`, native PostgreSQL 18 under `/usr/lib/postgresql/18/bin`,
Caddy 2.11.4, k6 2.3.0, iproute2 and root privileges for disposable namespaces
and systemd units. WSL Ubuntu with systemd is suitable for local functional
qualification. Allow enough free host memory for the generators in addition to
the system under test. Run from the exact reviewed checkout; the orchestrator
compiles three native Go binaries with CGO disabled before each campaign.

```bash
sudo env "PATH=$PATH" python3 src/Operations.Performance/performance_cli.py run --profile smoke
sudo env "PATH=$PATH" python3 src/Operations.Performance/performance_cli.py run --profile nominal
```

The application, database and Caddy share a dedicated **768 MiB**, **zero swap**,
**one logical CPU** cgroup. Generators and the observer are outside that budget.
Each generated run owns a dedicated systemd slice and four transient services.
The observer reads the parent cgroup's effective files, actual OOM counters and
service restart counts. A failure to apply or verify limits prevents qualification.
Existing application units, PostgreSQL clusters and parent controllers are untouched.

The API and worker run with production security settings, native Go binaries,
JSON logging and their configured 256/192 MiB Go memory limits. The email provider
is Disabled and Google is disabled. Email throughput is not qualified. The network
namespace contains only loopback and no external route; it cannot reach providers
or host services. HTTPS verifies the run-specific Caddy CA without installing it
in the host trust store. No production credentials, real data or backup repository
are used.

Observability remains enabled; its rolling private snapshots live in the disposable
application directory owned by the unprivileged service account. Service logs are
discarded; generator diagnostics remain private. The local filesystem and WSL
differences do not qualify VPS disk performance.

Preparation creates a fresh marked database with 100 members, 500 lists and 10,000
wishes, then uses real HTTP login, CSRF, sharing and upload contracts to initialize
100 gift images, 100 profile images, participations and reservations. Passwords,
JWT signing material and share proofs are disposable and stay in private local
files/memory. Login and refresh behavior is not bypassed. Long scenarios renew
tokens with the original application lifetime.

The root-owned `/tmp/<run-id>` directory is 0711 to permit traversal by isolated
service accounts; credential files are root-only 0600 and service data directories
are 0700. The frozen harness and non-secret Caddy configuration are readable by
the services. Existing historical CodeQL reviews remain historical evidence;
no scanning rule is disabled.

## Additional profiles

Each profile creates a new isolated dataset. Do not execute multiple performance
campaigns simultaneously when comparing measurements.

| Profile | Behavior |
|---|---|
| smoke | 30 seconds of the nominal request mix; functional CI check, latencies diagnostic only, not hardware qualification |
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

Results are under private `/tmp/<run-id>/`. Copy only sanitized reports to the
ignored `TestResults/performance/` directory when retaining local evidence.
`report.json` has schemaVersion 1, merged percentiles, counts, resource samples,
limits, dataset invariants and passed/failed/incomplete verdict.
`report.md` is the human summary. Image IDs, revision, local host facts and harness
SHA-256 fingerprints distinguish runs. Native binary hashes and Caddy/k6 versions
replace image identifiers. Harness files are frozen for each new run.

Only sanitized metrics and reports may be attached to a MR. **Never upload the
entire result directory**: environment files and fixture credentials are private.
Do not enable k6 HTTP debug logs. Public metric tags are fixed family/phase labels;
no headers, cookies, body content or token-bearing URLs are retained in reports.

After reviewing the evidence, stop this run's disposable services and remove its
network namespace and runtime slice configuration by its printed identifier:

```bash
sudo env "PATH=$PATH" python3 src/Operations.Performance/performance_cli.py cleanup --run-id mk816-012345abcdef
```

Cleanup checks the exact generated identifier and its root-owned ownership marker.
It targets only that run's services, slice and namespace. Stopped fixture data and
reports remain locally for diagnosis. A failed or interrupted run must not be
reported as successful.

If a target fails, retain the evidence and describe the bottleneck. Architecture,
business migrations, production quota changes or VPS upgrades require a separate
decision. No merge, deployment or secret rotation is performed by this harness.
