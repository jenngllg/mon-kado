# Native local operations qualification

Run `sudo env "PATH=$PATH" MONKADO_NATIVE_BINARIES=/absolute/reviewed/binaries bash scripts/Test-NativeOperations.sh`
from the reviewed checkout. The binary directory contains native Linux `api`,
`worker` and `migrate` executables. PostgreSQL 18 tools must be available under
`/usr/lib/postgresql/18/bin`; Caddy 2.11.4, Python, curl, iproute2, util-linux and
systemd are required. No Docker or external provider is involved.

Each HTTPS/installer test runs in a fresh anonymous network namespace with only
loopback and no external route. Caddy's private CA is verified by the test clients
and never installed in the host trust store. Installer targets are mapped to new
temporary directories in a private child mount namespace. Existing services,
accounts, PostgreSQL clusters, credentials and application data are preserved.

The suite verifies API security headers, forwarded addresses, redirects, strict
SNI/host handling, private error logging, frontend caching and forbidden paths,
legal pages during an API outage, publication rollback and process interruption,
PostgreSQL credential rotation, installer preservation/lockouts and bounded
monitor state. A real disposable systemd memory failure must be observed once
through the public collector. The monitor resource exercise runs sixteen virtual
clock cycles under 128 MiB, no swap and 10% of one CPU, without real email.

The deployment smoke runs its actual technical and functional probes against Go,
native PostgreSQL and Caddy with production cookie policies. It verifies login,
CSRF, CORS, TLS, create/read/update/delete and exact cleanup. A confirmed synthetic
account is seeded only into the fresh fixture. No production setting or provider
credential is accepted by the runner.

The separate native Go process suite also verifies browser journeys, worker local
delivery, export ZIP privacy, graceful shutdown and startup rejection. See
`tests/Native.ProcessTests/run.sh`. Performance acceptance and its longer profiles
are documented in `docs/operations-performance.md`; these functional checks do
not establish production hardware performance.
