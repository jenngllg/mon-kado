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

Each completed profile above has zero unexpected errors, rate-limited requests,
dropped iterations, OOM kills and restarts. The three minutes of warmup are
excluded from these measured counts. Image cases cover gift/profile uploads at
2 MP, near 10 MiB and 40 MP, first singly and then with two concurrent uploads.
Decoded output, downloads, image format and stable content are verified.
The 25-minute export measurement verifies one fresh export followed by two fresh
concurrent exports, completing in about 2.02 seconds each. ZIP size/CRC, member
identity, five lists, one hundred wishes and every referenced image are checked.

Nominal p95 is 1.69 ms for lists, 13.93 ms for wishes, 16.16 ms for shared lists,
13.78 ms for wish updates and 13.46 ms for reservation/cancellation requests.
During heavy image work, read p95 is 1.76/10.77/13.37 ms respectively. Existing
throughput and latency thresholds remain unchanged.

The early nominal/image report metadata incorrectly labels the worker environment
as `local`; their frozen fixture configuration actually sets `APP_ENV=production`
for both services. This metadata defect is corrected in the current harness.
Original reports are preserved unchanged. Their source is explicitly marked
dirty, and binary/harness fingerprints distinguish them from a published release.

Quota, concurrency and stress qualification is still in progress. A
profile is included as passed only after its full duration, final invariants and
supplemental checks succeed. The latest backend subsequently fixes conditional
guest display-name validation; the recorded earlier measurements exercise
authenticated enrollment and do not claim to test that corrected negative path.

These are local measurements, not production hardware acceptance or a comparison
with historical .NET/Docker timings. They do not qualify backup/deployment overlap,
Internet latency, visual frontend rendering or live external providers. The
[historical MK-938 findings](mk938-2026-09-26.md) remain unchanged.
