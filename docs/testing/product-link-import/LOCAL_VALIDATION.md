# Local validation

The final backend passed native PostgreSQL integration tests, importer and image
tests, audit-command tests, both Go module test suites, and `go vet` on Windows.
The final run explicitly disabled the Go test cache and serialized packages:

```powershell
$env:GOFLAGS = '-p=1 -count=1'
./scripts/Test-Native.ps1 -Coverage
```

Statement coverage was **87.77% (8115/9246)** in the application module and
**87.50% (70/80)** in the autonomous Gmail tool, above the existing 80% gates.
This is statement coverage, not branch coverage. `go mod verify`, `go mod tidy`
and the scoped formatting/diff checks also passed.

The frontend passed 4,929 unit tests, ESLint, all three TypeScript configurations,
production build and generated types against the served OpenAPI document.
Its existing coverage gates passed: 98.16% lines, 94.99% statements, 94.56%
functions and 89.79% branches. The complete controlled Chromium suite passed
279 tests without retries; 18 import, owner and extension scenarios passed again
after the final frontend text/image preservation changes.

Real browser evidence separately verifies creation (201), normalized image
upload and rereading (200), including IKEA's description, colour, exact structured
dimensions, EUR price and photo. Amazon, Rituals, Cdiscount recovery, mobile and
surprise mode have explicit screenshots and safe request/response records in the
companion frontend PR. Controlled browser fixtures do not prove merchant support.

## Windows database timing observation

Two repeated full runs encountered the existing 30-second migration database
cleanup deadline. PostgreSQL recorded forced checkpoints lasting 36.504 and
33.637 seconds, including more than 30,000 file sync operations. The failing test
passed in isolation; package serialization alone did not prevent the accumulated
I/O spike. The failing assertion was cleanup, not a migration or import assertion.

Only the isolated local cluster's `checkpoint_timeout` was changed from five
minutes to 30 seconds to spread that I/O. `fsync` remains on. The final complete,
uncached run then passed with package serialization. No application test timeout,
retry policy, production configuration or durability check was changed. Aborted
overlapping diagnostic launches were excluded from the reported passing run.

Merchant observations are in [the 30-site matrix](README.md) and
[raw final results](results.json). They retain eight complete imports, two partial
imports and twenty unavailable pages in the tested direct-provider configuration.
