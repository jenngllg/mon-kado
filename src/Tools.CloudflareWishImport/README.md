# Authenticated Cloudflare wish-import relay

This Worker supplies bounded public product HTML to the existing
Go importer. The browser continues to use the authenticated wish-import-preview
endpoint; no Cloudflare credential or merchant page is sent to frontend code.

The Go connector is disabled by default. `WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED=true`
and a random 32-byte hexadecimal `WISH_IMPORT_LOCAL_CLOUDFLARE_KEY` enable it only
when `APP_ENV=local`. Configuration rejects this combination in production, and
the API composition root independently excludes it in production. The connector
only calls `http://127.0.0.1:8787/preview`, ignores process proxies, refuses relay
redirects and allows two seconds across at most two relay attempts. It has no
direct merchant-page fallback. Image downloads retain their pinned direct transport. Only
temporary failures permit a second attempt; permanent refusals and redirects do not.
The operator's
ephemeral preview adapter must bind that loopback endpoint and inject the same key
as the Worker's `TEST_AUTH` secret. Never put either credential into Vite settings.

Every admissible public merchant uses Cloudflare first, including a hostname never
seen before. No per-merchant registration or remembered routing decision is needed.
The original six-host allowlist has been removed at Jenn's explicit request.
The relay uses standard passive metadata extraction; no merchant-specific
markup selectors or public storefront catalogue remains.
Old HTTP links are upgraded to HTTPS on port 443 for the edge attempt. The Worker accepts HTTPS only
and rejects credentials, IP-literal URLs, local/special hostnames, unknown fields and all
redirects. It validates complete public A/AAAA answer sets using a fixed HTTPS DNS
endpoint, rejects private/special/mapped addresses and mixed DNS, sends only the
application User-Agent to merchants, and bounds JSON, DNS, HTML and elapsed time.
No browser cookies, authorization headers, response cookies or active scripts are
executed or forwarded to the user. The backend retains the existing bounded
image download, IP pinning, normalization, caller cancellation and HTTP contract.

## Security boundary

DNS preflight is **not IP pinning**. Workers `fetch` chooses its final connection
address, and `resolveOverride` cannot pin arbitrary merchant zones. Jenn explicitly
extended this managed-network exception to production on 27 October 2026, recorded
in `AGENTS.md`. Direct/provider connections still require public-DNS validation and
IP pinning. Merchant DNS is checked before and after both HTML and image retrieval.

Permanent deployment uses `WISH_IMPORT_MANAGED_ENABLED`, an authenticated dedicated
`https://monkado-wish-import.<account>.workers.dev` origin and its `IMPORT_AUTH`
secret binding. Its Go client calls that fixed authority through the pinned HTTPS
transport, without redirects or process proxies. The secret never enters browser
code or merchant headers. Default-off local loopback configuration remains separate
and forbidden in production. The legacy `TEST_AUTH` binding is accepted only as an
alternative when `IMPORT_AUTH` is absent; a malformed production secret fails closed.

The explicitly enabled local `/image` experiment accepts the same authenticated
JSON URL request and returns at most ten MiB of static-image bytes. It sends only
the application User-Agent and supported-image Accept header, refuses redirects
and active formats, and checks complete public DNS sets before and after fetching.
The Go caller revalidates public DNS and normalizes/strips the bytes locally.
This remains a DNS-validation exception, not merchant IP pinning. The independent
local Firecrawl binary fallback is documented in `docs/wish-link-import.md`.

No live Worker deployment, production route, MonKado DNS change, database
migration or paid subscription is needed for an ephemeral local test.

## Separate local browser experiment

`browser-worker.mjs` is a separate, explicitly enabled local experiment; it does not
replace or update the production fetch Worker. Configure a dedicated
`monkado-wish-render-local.<account>.workers.dev` Worker with a `BROWSER` binding
and an independent random `IMPORT_AUTH` secret. Enable
`WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ENABLED` with its matching `_ORIGIN` and `_KEY`
only alongside managed imports in `APP_ENV=local`. Startup and composition reject
production use. No browser SDK or paid plan is required.

The shared preview pipeline tries fast Cloudflare fetch first, then this browser
only for incomplete suggestions, then the existing metered fallback. The Worker
uses at most six seconds and its pinned Go connector at most seven including
transport, always inside the shared eight-second preview deadline. It refuses to
queue when another session is active, an acquisition is unavailable or reported
daily usage exceeds 500 seconds, leaving reserve within the free ten-minute quota.
The complete-preview cache and exact query identity remain unchanged.

Each browser is anonymous, clears cookies, and closes only its own acquired
session. Document/resource DNS is validated before continuing and again before
returning; redirects, private authorities, credentials, unsafe methods, workers,
frames and popups are denied. The response CSP keeps scripts in the intercepted
page. Requests, hosts, protocol payloads, total network bytes and HTML are bounded.
One declared Open Graph image may reuse that same session, limited to one request,
one extra second and two MiB. Go accepts its bytes only for a generic parser image
candidate with public DNS, then normalizes and strips metadata through the usual
image pipeline. Image failure preserves usable product text. No credentials or
active merchant markup reach the frontend. DNS preflight remains the existing
explicitly authorized managed-network exception, not merchant IP pinning.

Run the deterministic Worker tests with `node --test *.test.mjs`. Go connector
and environment-boundary tests live under `internal/importer`, `internal/config`
and `internal/httpapi`; their providers are fakes and never contact merchants.
