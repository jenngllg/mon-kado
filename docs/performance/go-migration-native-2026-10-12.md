# Local native Go performance evidence

Recorded on 2026-10-12 using native Go 1.27.2, PostgreSQL 18, Caddy 2.11.4 and
k6 2.3.0 under WSL Ubuntu with systemd/cgroup v2. API, worker, PostgreSQL and
Caddy share **one logical CPU, 768 MiB and zero swap**. Each run has a fresh
100-account, 500-list, 10,000-wish dataset and 200 initial images. Providers are
disabled; its private loopback-only namespace has no external route.

The [sanitized machine-readable evidence](go-migration-native-2026-10-12.json)
retains run identities, binary digests, source revisions, dirty-state flags,
original report digests, counts, p95 latencies, supplemental results and data
invariants. Original private reports remain in the operator's ignored local
directory. Credentials and environment files are never part of this evidence.

| Profile | Measured requests | Observed requests/s | Peak shared memory | Result |
| --- | ---: | ---: | ---: | --- |
| Nominal | 9,000 | 10.0061 | 173.95 MiB | Passed |
| Heavy images | 9,000 | 10.0041 | 574.54 MiB | Passed; all 18 supplemental cases |
| Exports | 15,000 | 10.0016 | 177.29 MiB | Passed; three fresh ZIP exports |
| Quotas | 1,800 | 10.0395 | 175.59 MiB | Passed; enforced limit and timed recovery |
| Concurrent writes | 1,800 | 10.0399 | 178.27 MiB | Passed; ETag and reservation conflicts |
| Stress and recovery | 12,000 | 10.0116 overall | 176.79 MiB | Passed; all four stage targets |

Each completed profile above has zero unexpected errors, rate-limited requests,
dropped iterations, OOM kills and restarts. The three minutes of warmup are
excluded from these measured counts. Image cases cover gift/profile uploads at
2 MP, near 10 MiB and 40 MP, first singly and then with two concurrent uploads.
Decoded output, downloads, image format and stable content are verified.
The 25-minute export measurement verifies one fresh export followed by two fresh
concurrent exports, completing in about 2.02 seconds each. ZIP size/CRC, member
identity, five lists, one hundred wishes and every referenced image are checked.
The quota supplement separately reaches the aggregate per-IP limit, verifies
429/no-store and a 60-second Retry-After, then verifies successful recovery after
that advertised deadline. Its expected 429 is separate from the measured business
request error counters.
Concurrent updates using the same ETag produce exactly one 200 and one 412,
with the winning value persisted. Competing reservations for the full remaining
quantity produce exactly one 201 and one 409, with no over-reserved wishes.

All six profiles pass **48,600 measured business operations**. Stress has four
five-minute stages at 5/10/20/5 requests per second; their observed completion
rates are 5.0263/10.0223/20.0145/5.0262. All ten generators finish successfully,
and final dataset counts remain 100/500/10,000 with zero over-reserved wishes.
Stress p95 is 1.72/12.50/15.05 ms for list/wish/shared reads and 12.93/13.29 ms
for updates/reservations. The stress profile keeps latency diagnostic by policy;
all observed latencies nevertheless meet the ordinary thresholds. Its clean
source revision is `759d4657244c1c3eac5bdf42f8533cf024ce48a7`.

Nominal p95 is 1.69 ms for lists, 13.93 ms for wishes, 16.16 ms for shared lists,
13.78 ms for wish updates and 13.46 ms for reservation/cancellation requests.
During heavy image work, read p95 is 1.76/10.77/13.37 ms respectively. Existing
throughput and latency thresholds remain unchanged.

The early nominal/image report metadata incorrectly labels the worker environment
as `local`; their frozen fixture configuration actually sets `APP_ENV=production`
for both services. This metadata defect is corrected in the current harness.
Original reports are preserved unchanged. Their source is explicitly marked
dirty, and binary/harness fingerprints distinguish them from a published release.

Each profile is recorded as passed only after its full duration, final invariants
and supplemental checks succeed. The backend fixes conditional guest display-name
validation after the earlier nominal/image/export measurements; those runs
exercise authenticated enrollment and do not claim to test the corrected negative
path. The later quota/concurrency/stress runs include that correction, which also
has explicit public HTTP integration tests.

The original evidence used an `api` field for a public executable digest, which
Gitleaks classified as a generic API key. The current schema represents executable
digests as service/SHA-256 records. Historical scanning allows only the three
exact public digests at this evidence path; actual credential detection remains
enabled throughout the repository and history.

These are local measurements, not production hardware acceptance or a comparison
with historical .NET/Docker timings. They do not qualify backup/deployment overlap,
Internet latency, visual frontend rendering or live external providers. The
[historical MK-938 findings](mk938-2026-09-26.md) remain unchanged.
