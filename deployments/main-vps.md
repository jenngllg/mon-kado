# Production from main

`main` is the production branch of both public repositories. `develop` remains
integration-only. Standard Ubuntu GitHub-hosted runners are free for these public
repositories; this procedure adds no paid service, runner, registry or subscription.
Production is automatic after successful checks, without a manual environment
approval or wait timer. The environment remains restricted to main; protected
branches and all artifact, revision, backup and online compatibility checks remain.

## Release path

1. A push/merge to `main` runs quality checks. Backend publication waits for both
   CI and all four CodeQL analyses for the exact main commit. Frontend publication
   waits for its complete quality gate and exact native backend contract proof.
2. The `production` environment job starts automatically. It publishes immutable
   release assets, then requests the actual VPS rollout through native OpenSSH.
   Publishing assets alone is not reported as a successful VPS deployment.
3. The root-owned VPS entrypoint verifies the protected channel, installed tool
   inventory, current main revision, immutable artifacts and compatibility. It
   pins the complete approved pair, then captures and verifies one fresh encrypted
   offsite backup before either component switches. Both changed components share
   that operation's snapshot. A failed capture, transfer or integrity check blocks
   both switches.
4. Backend replacement starts and probes a candidate on loopback port 7001. A
   root-owned file selector moves new requests to it. The old API drains before
   its replacement starts on port 7000. Requests then return to the stable port.
   Caddy stays running and is not reloaded during normal deployments. Native
   functional and persistence checks must pass. The frontend switches atomically;
   reachable assets from the last three published frontends remain available to
   already-open tabs.

Each workflow publishes its immutable assets and protected pointer before waiting
up to forty minutes for both pinned main publications, with a sixty-minute
publication job budget. A full Go verification can exceed ten minutes. Failed or
cancelled exact-revision quality checks abort the wait promptly. This uses the existing
GitHub workflow token, outside the VPS lock. Frontend packaging still requires the
exact native backend contract proof. Any changed main revision, missing proof or
unpublished/failed peer blocks the group before backup or switch. Merge backend
then frontend immediately afterward so the frontend CI tests the intended backend.
Polling backs off after two minutes so a full wait leaves room in the standard
workflow token's free API budget; no additional credential or paid capacity is used.

The first VPS request deploys the complete pair under the shared lock. The second
request finds it already installed and takes no further backup. Backend-only and
frontend-only changes still deploy automatically with one new backup. A revision
arriving after the pair was pinned belongs to a later operation with a new backup;
no time-based reuse of previous snapshots is allowed. Both revisions, checks and
the shared snapshot are recorded in the durable receipt. Unchanged SQL is required.

Version-2 receipts use the same non-null `backendSnapshot` and `frontendSnapshot`
when both components change. Recovery refuses a shared capture older than the
existing one-hour freshness limit; it leaves the compatible running services in
place and retains the pinned intent for explicit recovery. A new unrelated backup
must never be substituted into that operation. Version-1 receipts keep their
historical meaning.

The two selector routes use connection-failure fallback and disable upstream
keepalive. They retain Caddy's safe default of not retrying a transmitted unsafe
request. No database mutation is replayed to hide an uncertain response.

Automatic online rollout currently requires an unchanged SQL catalog and preserves
all existing API operations/schemas. New endpoints can be additive. SQL changes or
incompatible contracts fail with an explicit review-required code before serving
changes; the running site remains available. Review an online-compatible migration
procedure before releasing such a change. Never silently fall back to the old
stop-the-site deployment path.

## Avoid repeated work

Backend CI runs Go verification/release builds and native deployment/recovery
verification on two independent standard Ubuntu runners. The required `quality`
gate waits for both and rejects failed, cancelled or skipped jobs. Each runner
has its own native PostgreSQL fixture; deployment tests compile the same revision
locally, without adding an intermediate artifact or weakening namespace isolation.
The existing release and qualified Caddy artifacts retain their names and retention.
CodeQL, security scans, race/coverage checks, native HTTPS/continuity journeys and
encrypted restore tests remain required before publication. Production backup and
the online switch are unchanged; additional parallel compute remains free for this
public repository. Queue, full-test and offsite-provider latency still vary.

Publication uses binaries/build files from the successful CI run for the exact
commit, rather than rebuilding them. Native Go race/coverage verification can be
reused for up to six hours only if every tracked Go module, executable/test source,
embedded resource, migration, toolchain declaration and PostgreSQL initializer is
identical. The original proof's timestamp is retained, never renewed by reuse.
Missing, expired or changed evidence runs the full suite. Operational tests, native
journeys, vulnerability scans, coverage thresholds and CodeQL still run.

Build artifacts are short-lived. If a manual publication's artifact expired, rerun
quality checks; do not rebuild unqualified code in the publication job.

## VPS access

Each repository's `production` environment holds its own `VPS_DEPLOY_SSH_KEY`.
Public environment variables are `VPS_HOST`, `VPS_USER` and
`VPS_SSH_KNOWN_HOSTS`. Host keys are pinned from an already verified connection.
Never use `StrictHostKeyChecking=no`, an administrator password or an unrestricted
administrator key in CI.

The `monkado-ci` account has root-owned SSH authorization with `restrict` and a fixed
`monkado-ci-trigger backend` or `frontend` command. The caller supplies only a
40-character commit SHA. The forced command can invoke only the reviewed root
coordinator's `main-deploy` operation; it cannot execute arbitrary shell commands,
forward ports or choose a backup/database operation. Both repositories serialize
requests using the VPS coordination lock.

### Network access and administrator isolation

GitHub-hosted runners have changing source addresses. The DigitalOcean cloud
firewall must let their IPv4 TCP connections reach port 22. A cloud rule limited
to the administrator's home address silently times out before SSH authentication.
Do not solve this by giving CI an administrator key or by placing a general
GitHub Actions runner on the public application's production host.

The server retains the administrator source restriction in a private root-owned
`/etc/ssh/sshd_config.d/60-monkado-ci-access.conf` (mode 0600):

```text
AllowUsers *@<administrator-ip>/32 monkado-ci
```

Use the actual administrator source allowlist from the former cloud rule; keep
these private addresses out of Git. This preserves existing user access from
that address and accepts only `monkado-ci` elsewhere. Root login, password
authentication and keyboard-interactive authentication remain disabled. The CI
account still requires one of its forced-command keys; arbitrary commands,
interactive shells and forwarding remain unavailable.

Before changing production configuration, take and verify a fresh encrypted
offsite backup. Check the proposed configuration with `sshd -t`, install it,
check the effective `sshd -T` settings, and reload `ssh.service` without restarting
it. Keep an existing administrator session open until a new connection succeeds.
Before broadening the cloud rule, verify that a connection whose source is outside
the administrator allowlist rejects the administrator's valid key, accepts each
CI key, and rejects an arbitrary CI command. An SSH tunnel through the existing
administrator connection to `127.0.0.1:22` tests this policy without copying any
private key to the VPS. Then verify actual GitHub-to-VPS delivery for both repos.

If the administrator address changes, update this private source pattern through
an existing authorized session or the provider console, validate and reload SSH.
For rollback, restrict the cloud rule back to the administrator allowlist first;
only then remove the private SSH drop-in and validate/reload the service. No API
or Caddy restart is needed for these SSH changes.

## Live backup and recovery

The native backup pauses the deletion worker while the API and Caddy keep serving.
An exported PostgreSQL MVCC snapshot supplies both the dump and its exact image
references. A bounded shared table lock fences reference-changing writes briefly;
reads continue. Referenced immutable images are hard-linked into the private
capture before releasing that fence, so a later deletion cannot remove captured
bytes. Uncommitted uploads are excluded. A failed or timed-out fence aborts the
deployment. Integrity verification and encrypted offsite transfer are unchanged;
the worker resumes and database locks are released before offsite transfer.

Receipts are private files named `online-receipt-*.json` under
`/var/lib/monkado-deployment`, including both source revisions and snapshot IDs.
Interrupted work retains `online-transition.json`. Resume the pinned request with:

```sh
sudo /opt/monkado/deployments/production/monkado-coordinate online-recover
```

An unchanged-schema failed backend can roll back its binaries through the same
online API handoff. No database rollback or reset is available. A failed frontend
activation leaves the running backend and old compatible frontend available and
retains the exact candidate for recovery. Recovery revalidates the original shared
snapshot and refuses expired or uncertain evidence before switching the frontend;
it never substitutes a different capture. Legacy version-1 intents retain their
original two-capture recovery procedure. The routing selector is reconstructible; a
missing selector falls back to the stable API on port 7000.

For the one-time reviewed operational-tool transition, use the protected tool
publication from main and `coordinated_install_cli.py upgrade-online SOURCE`, with
fresh verified offsite captures before and after installation. Its initial Caddy
configuration reload is graceful; it never stops the API or proxy. Subsequent
application deployments only change the file selector. Resume an interrupted tool
transition with `recover-upgrade-online SOURCE`, using the same authenticated source.
