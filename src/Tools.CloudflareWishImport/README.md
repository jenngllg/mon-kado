# Local Cloudflare wish-import relay

This development-only Worker supplies bounded public product HTML to the existing
Go importer. The browser continues to use the authenticated wish-import-preview
endpoint; no Cloudflare credential or merchant page is sent to frontend code.

The Go connector is disabled by default. `WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED=true`
and a random 32-byte hexadecimal `WISH_IMPORT_LOCAL_CLOUDFLARE_KEY` enable it only
when `APP_ENV=local`. Configuration rejects this combination in production, and
the API composition root independently excludes it in production. The connector
only calls `http://127.0.0.1:8787/preview`, ignores process proxies, refuses relay
redirects and allows five seconds across at most two relay attempts. It has no
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
address, and `resolveOverride` cannot pin arbitrary merchant zones. Do not claim
otherwise or publish this relay as production-ready. Activating this development
connector, including all public merchant domains, is covered by Jenn's explicit local-only exception recorded in
`AGENTS.md` on 27 October 2026. It does not authorize production activation.
Direct connections keep their existing pinning requirement.

The explicitly enabled local `/image` experiment accepts the same authenticated
JSON URL request and returns at most ten MiB of static-image bytes. It sends only
the application User-Agent and supported-image Accept header, refuses redirects
and active formats, and checks complete public DNS sets before and after fetching.
The Go caller revalidates public DNS and normalizes/strips the bytes locally.
This remains a DNS-validation exception, not merchant IP pinning. The independent
local Firecrawl binary fallback is documented in `docs/wish-link-import.md`.

No live Worker deployment, production route, MonKado DNS change, database
migration or paid subscription is needed for an ephemeral local test.

Run the deterministic Worker tests with `node --test worker.test.mjs`. Go connector
and environment-boundary tests live under `internal/importer`, `internal/config`
and `internal/httpapi`; their providers are fakes and never contact merchants.
