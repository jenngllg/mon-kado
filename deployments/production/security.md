# Backend security operations (MK-814)

## Boundaries and release status

The canonical frontend origin is `https://www.monkado.fr`; the API is
`https://api.monkado.fr`. CORS does not authorize callers. Requests without an
Origin header still require the endpoint's normal authentication and authorization.
Do not add the apex domain, GitHub Pages or localhost to production merely to make
a browser error disappear. MK-811 must redirect the apex frontend to `www`.

Only Caddy publishes TCP 80/443. PostgreSQL and the API stay on Docker networks.
Only the dedicated edge network may supply forwarded IP/protocol information;
the API processes one proxy hop. Do not add a CDN or another proxy without
revisiting this trust chain. Caddy checks TLS SNI against Host. Health endpoints
must use the correct hostname. HTTP redirects to HTTPS; HSTS is one year without
includeSubDomains/preload. Do not enable preload before every domain is ready.
Unknown HTTP hosts receive 404 instead of a redirect derived from their Host
header. This catch-all preserves Caddy's hostname-matched automatic redirects
([Caddy routing order](https://caddyserver.com/docs/automatic-https#effects)).

The API CSP deliberately prohibits all executable page content. It is not a CSP
for the frontend document. Caddy also applies the same response protections to
its upstream errors, overwriting rather than duplicating upstream headers. Proxy
errors remain proxy responses, not simulated application ErrorResponse objects.

## Authentication and CSRF matrix

All paths below are under `/api/v1` unless stated otherwise. The controller
attributes and generated OpenAPI remain the authoritative per-operation contract.

| Endpoint family | Authentication or proof | Mutation CSRF rule |
| --- | --- | --- |
| Registration, e-mail confirmation and password reset | Anonymous token/password flows | Existing antiforgery required |
| Session creation, refresh, logout | Credentials or refresh cookie | Antiforgery required |
| Current session | Bearer | Read only |
| Google challenge | OIDC challenge | GET, middleware state/correlation |
| Google callback | Validated OIDC response | Middleware state, correlation and nonce; no API antiforgery |
| Google completions/link | Temporary external cookie plus body binding | Antiforgery required |
| Two-factor completions/setup/confirmation | Bound proof, and original Bearer where required | Antiforgery required |
| Two-factor recovery-code regeneration and account management | Current Bearer plus relevant business proof | Bearer-only; cookies alone insufficient |
| Owner lists, gifts, image upload/removal, imports, share management | Bearer and resource ownership | Bearer-only |
| Personal exports and administrative operations | Bearer; administrator checked in PostgreSQL | Bearer-only |
| Shared-list reads and participant/reservation reads | Share proof, optional Bearer/guest cookie | Read only; invalid Bearer must fail, not become guest |
| Shared participation and reservation mutations | Share proof, optional Bearer/guest cookie | Antiforgery required for every caller |
| Shared-list reports | Share proof, anonymous report | Existing antiforgery required |
| Signed image URLs / public member lookup | Existing signed proof or public-read contract | Read only |
| `/security/csrf-token` | Anonymous token bootstrap | GET, no-store; returns request token while cookie remains HttpOnly |

Keep production cookies Secure, HttpOnly, host-only and with their current Path
and SameSite attributes. OIDC correlation/nonce cookies have their own cross-site
requirements; do not globally replace them with Strict. The browser must use
credentials when obtaining and submitting CSRF tokens. Never put refresh tokens
in JSON or widen cookie Domain to the parent domain.

## Two layers of request quotas

The perimeter middleware runs after trusted proxy processing/CORS and before
Bearer validation, database-backed identity checks and business quotas. It limits
`/api/v1/**` and `/security/csrf-token`, including unmatched API routes. OPTIONS,
health checks and OpenAPI are excluded. IPv4-mapped IPv6 is normalized to IPv4;
requests without an address share the `unknown` partition. No request is queued.

`GeneralRateLimit:PermitLimit=300` and `GeneralRateLimit:WindowSeconds=60` are
startup-validated defaults. Compose exposes `GENERAL_RATE_LIMIT_PERMIT_LIMIT`
and `GENERAL_RATE_LIMIT_WINDOW_SECONDS`. Capacity must be positive; the window
must be 1–3600 seconds. Change them only with a reviewed restart. Counters are
singleton process memory, reset on restart and not shared between replicas.
This is neither a distributed quota nor DDoS protection. Clients behind a shared
address also share the perimeter allowance.

Existing business quotas remain unchanged. All six member-scoped policies
(gift/profile uploads, URL imports, account deletion confirmation, administrative
erasure and administrative session revocation) authenticate the Bearer before
selecting their partition. Two valid members behind one IP retain independent
business allowances. The perimeter allowance still bounds their combined traffic.

Both layers return structured 429, Retry-After in seconds, no-store and the
existing REQUEST_RATE_LIMIT_EXCEEDED code. No token, address or support reference
is added to rejection logs. Global throttling may reject a request before its
authentication error is evaluated; this is intentional to avoid database work.

## Secrets inventory and recovery

| Material | Consumers | Authoritative storage / recovery |
| --- | --- | --- |
| PostgreSQL password | PostgreSQL, migrations, API, Worker | root-only production.env; encrypted backup |
| JWT signing key | API | root-only production.env; encrypted backup |
| Gmail client/grant | Worker | root-only production.env; separate provider authorization |
| Google login client secret | API when explicitly enabled | root-only production.env; Google client configuration |
| Data Protection key ring | API and Worker | persistent volume; complete encrypted backup including revocations |
| Backup encryption secret | Backup/recovery tools only | dedicated root-only file and independent Bitwarden recovery copy |
| Drive OAuth grant | Backup/recovery tools only | separate restricted grant; never substitute the Gmail grant |
| TLS private keys | Caddy only | Caddy volume; certificates can be reissued under operator control |

Development uses User Secrets, never checked-in appsettings. `production.env`
must be a regular root-owned 0600 file in its protected directory. Access to
Docker is equivalent to root; environment variables are not protected from a
Docker administrator. Do not run or share expanded `docker compose config`,
full `docker inspect`, shell tracing or process environments. Configuration
examples contain names/defaults only. The build context excludes local folders,
dotenv, production.env, OAuth downloads, rclone configuration and private keys.

Safe host inspection (metadata only; no contents):

```sh
sudo stat -c '%U:%G %a %F' /etc/monkado/production.env
sudo test ! -L /etc/monkado/production.env
sudo systemctl show monkado-deploy.service -p ExecStart
sudo systemctl list-timers 'monkado-backup*' --no-pager
```

Do not install production secrets into GitHub Actions, images, logs or this
repository. No new deployment SSH key, PAT, paid vault or runner is needed.

### Replacement rehearsal and incident procedure

Rehearse only with synthetic values and isolated containers first. Real
replacement requires explicit operator approval and a recoverable backup.

* **JWT key:** use a new independent 32-byte-or-longer Base64 key. Validate config,
  replace privately and restart all API instances together. Old access tokens
  must fail signature validation; valid refresh sessions can obtain newly signed
  tokens. Rotation alone is not session revocation. For compromise, additionally
  revoke affected sessions using existing operations; do not edit JWT claims.
* **PostgreSQL:** coordinate downtime for API/Worker/migrations, change the role
  password interactively (`psql` `\password`, not a literal SQL secret in history),
  update the private dotenv and restart consumers. Changing POSTGRES_PASSWORD in
  Compose alone does not alter an existing database. Verify new connections and
  health. Recovery must align the database credential and dotenv, not just revert
  one side. Do not print connection strings on failure.
* **Gmail/Google/Drive:** revoke/re-authorize only the intended client/grant,
  install privately, restart its consumer and verify refresh and bounded failures.
  Gmail delivery uses its outbox, never blind replay. Google remains disabled
  pending its approved real HTTPS/browser smoke test. Drive access and its
  encryption password are separate from Gmail credentials.
* **Data Protection:** normal rotation adds keys; keep existing keys and
  revocation records. Never delete the ring as a troubleshooting step: protected
  MFA material, temporary proofs and cookies may become unreadable. Restore or
  revocation is a separate explicitly approved incident operation, with its
  authentication consequences documented. Keep MK-813 recovery access intact.

The HTTP suite checks rejection of wrong signing keys, cookie-only access and
invalid temporary proofs. The isolated storage and cryptography tests must stay
green; provider grant replacement is simulated, never performed on real accounts
by the quality gate.
The Docker rehearsal in `tests/Deployment.SecurityTests/test_secret_replacement.py`
replaces a synthetic PostgreSQL role password, checks old/new connections and
restarts the container with its original startup environment. The new password
must still work and the old one must still fail; no production database is used.

## Read-only production verification and rollout

Run `python src/Operations.Security/security_probe.py` from a reviewed checkout.
It issues exactly four HEAD/OPTIONS requests to the fixed production API, verifies
TLS normally, does not follow HTTP redirects, prints booleans only and exits
nonzero if a check fails. It does not test quota exhaustion, log in, send mail,
rotate secrets or prove frontend/browser readiness.

Run real Caddy tests only in disposable local Docker resources:
`MONKADO_CADDY_TESTS=1 python tests/Deployment.SecurityTests/test_caddy.py`.
They use a local CA (certificate verification enabled), synthetic upstream,
loopback ports and exact generated cleanup targets; no production credentials.

Caddyfile and Compose changes alter the deployment configuration fingerprint.
Use the existing approved publication/reinstallation sequence; never bypass the
hash or pull unreviewed files into /opt/monkado. Before/after installation, verify
the MK-813 deployment guard drop-in, backup timers and locks remain unchanged.
Do not overwrite or import the unmerged backup branch. Keep the current release
and configuration available; automatic database rollback is outside this US.

Production rollout, actual secret rotation, merge and Google activation are not
authorized by this document. MK-811 must still validate real browser credentialed
CORS/CSRF, direct routes, frontend CSP and Google return navigation; API headers
alone cannot complete that validation. A meta CSP on GitHub Pages has limitations,
including no frame-ancestors enforcement; select its policy from actual frontend
resources without introducing unsafe-inline/unsafe-eval merely to silence errors.
