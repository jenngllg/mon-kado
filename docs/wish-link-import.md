# Wish link import (#988)

The existing authenticated preview endpoint remains unchanged. It returns suggestions only: no wish or image is persisted until the member submits the form.

## Merchant support

All merchants use the same passive extraction: JSON-LD, schema.org microdata/RDFa,
Open Graph/Twitter product metadata and validated title/headline fallbacks. Unicode
names longer than the public 100-character field are shortened at a word boundary.
Equivalent Product descriptions and ProductGroup variants are resolved using the
requested and canonical URLs, SKU and shared entity identity. Structured offers
select an explicitly matching product/offer URL or SKU, never a cheapest or first
ambiguous variant. Equal variant prices, concrete offers, single-price aggregate
offers and active price specifications are supported without rounding or converting
foreign currencies. A range remains unset rather than becoming a guessed price.
Passive serialized variant data can supply an otherwise missing price only when
one product variant matches the requested URL and name, no multiple page choice
is visible, and the amount is corroborated beside the product heading.
When no structured product or price is available, a uniquely marked visible
euro price near a matching product heading can also be used. Conflicting values,
starting prices, product choice controls, old prices and linked or separate
recommendations are not used to infer that price.
When an aggregate starting price is displayed beside an explicitly unselected
size, the preview keeps the price empty without requesting another provider just
to guess it. The complete name and image can be reused by the bounded cache.
An explicitly displayed starting price also remains empty when metadata reports
the minimum as a concrete price for an unconfigured product.
The same safeguard applies when a clearly marked price alongside the product
heading disagrees with the structured price.
Multiple explicit, differently priced offers without a URL selection are likewise
kept unset and do not trigger a provider request solely to choose a price.
A page's default selection does not establish the user's choice when the link
identifies no variant; an incomplete price set can still be retried by a provider.
For ambiguous product/group identity, standard `utm_*`, `gclid` and `fbclid`
tracking parameters do not prevent recovery of the shared name and image. Other
query parameters may select a variant and remain subject to the usual selection rules.
Up to three ordered product-image candidates are downloaded and normalized inside
the existing budget; an image for a different selected variant is not substituted.
There are no hostname-specific selectors or external storefront
catalogues. Missing or ambiguous information remains available for manual entry.

For long galleries, the selected product's primary picture remains first; an
explicit Open Graph/Twitter preview is considered before the remaining gallery.
This keeps alternate, optimized preview URLs inside the three-download budget.
For an explicitly colored Product linked to a ProductGroup, a unique visible
high-priority product image may instead represent that selected color more
accurately than the group's structured gallery; ambiguous markup is ignored.
Responsive image descriptors retain commas inside URLs. When image metadata is
missing, a unique preloaded responsive image may be used only if its path
explicitly identifies the selected variant from the request.

The optional local browser experiment is documented in
`src/Tools.CloudflareWishImport/README.md`. It can recover dynamic public markup
and one declared picture after fast Cloudflare fetch, before Firecrawl, while
retaining the existing eight-second server / ten-second form ceilings. It is
default-off and independently refused in production. This does not change the
production workflow or establish a new 99-merchant success rate.

JSON-LD offers, images, variants and price specifications can reference entities
declared elsewhere in the same document, including another JSON-LD script.
Compatible definitions are combined; conflicting definitions, unresolved references
and cycles never select an arbitrary amount. Parsing and expansion have separate
depth/work budgets. No remote context or entity identifier is fetched. HTML itemref
properties and scoped RDFa use the same selection and monetary rules. Detached
offers require an explicit URL matching the product page; an unselected used item
or a recommendation cannot supply a guessed price. An unpriced colour stub can
use its same-page size offers only when no query selects a size and their amounts
agree; differently priced sizes and other colours remain ambiguous/excluded.

Merchant images also accept static AVIF, including alpha, grids and orientation,
through a native Go decoder. They are normalized to the existing metadata-free
WebP response. Upload formats and HTTP contracts are unchanged. AVIF sequences
are refused; before decoding, container structure, item counts and a conservative
16-million-pixel aggregate bound are checked in addition to the existing 10-MiB
input and two-slot processing limits. The codec also has a frame-area limit.
Cancellation is checked between the bounded decoding/normalization stages.

## Local Cloudflare-first test

The explicitly enabled development connector tries Cloudflare first for every
admissible public merchant, including a first-ever hostname. No merchant registration
is required. A temporary relay/merchant error permits one additional edge attempt
inside the same five-second edge budget. Permanent refusals, redirects, invalid
content and authentication failures are not repeated. There is no direct merchant-page
fallback in this local relay path. Failed relay imports keep explicit missing-field
warnings. DNS-pinned direct downloading remains necessary for product images.
The disabled-relay production path is preserved; no production routing change is
included. An explicitly enabled local Firecrawl fallback can complete suggestions
after the Cloudflare attempts. It is skipped when the primary import already has
a name, price and normalized image. Available primary fields are retained when
the final provider fails; failed image candidates are not downloaded twice.

Set `WISH_IMPORT_LOCAL_FIRECRAWL_ENABLED=true` and supply `FIRECRAWL_API_KEY`
privately to test this last provider. Startup requires local Cloudflare and rejects
production activation. The provider authority is fixed to `api.firecrawl.dev`;
its POST uses the public DNS/IP-pinned transport without process proxies or
redirects. No merchant cookie, header, credential or URL fragment is forwarded.
The raw HTML uses the same generic parser and pinned image pipeline.
Page requests explicitly accept Firecrawl cache entries up to 47 hours,
49 minutes and 50 seconds old, leaving room within Jenn's 48-hour preference
for the local preview cache and the bounded provider transfer;
this limits stale product prices compared with Firecrawl's two-day default.
Image requests retain their existing cache behavior. Cached Firecrawl requests
may still consume credits, independently of MonKado's own preview cache.

The optional `WISH_IMPORT_LOCAL_FIRECRAWL_ROLLING_ENABLED` experiment allows
bursts while bounding starts to ten per rolling minute within one API process.
It requires an authenticated Firecrawl fallback and is rejected in production.
This process-local guard does not coordinate other processes or account users;
provider rate limits and the remaining credit balance still apply.
When this local experiment and authenticated image recovery are both enabled,
the account image provider makes at most one attempt per preview after the
other image sources fail. A separate page request may also consume credits.
`WISH_IMPORT_LOCAL_FIRECRAWL_PRODUCT_ENABLED=true` additionally requests the
deterministic `product` format in that same call. Product URL identity, exact EUR
amounts, variant agreement and existing ambiguous offers are checked locally;
provider suggestions cannot silently override conflicting page prices. If the
primary page provides no usable suggestions, that same single fallback POST asks
for rendered HTML alone: optional enrichment must not hold up a document whose
ordinary schemas already contain the product fields. A partial primary preview
retains the additional product format. This choice depends on the available
fields, never the merchant, and does not mutate shared provider settings.

The server preview budget defaults to, and cannot exceed, eight seconds. This
reserves transport and browser processing time within Jenn's ten-second maximum
for the whole form analysis. Cloudflare has at most two seconds including its
single transient retry; the initial image phase uses at most one second. The
single Firecrawl page POST uses at most six seconds inside the remaining shared
deadline, reserving one second for images. Raw HTML and product enrichment use
the same budget. When a local browser renderer is enabled and only a usable
price is missing, its attempt is capped at four seconds and reserves up to
three seconds of the remaining budget for the configured final fallback. Former twenty/thirty-five-second
overrides are rejected.

Both create and edit forms debounce input for 250 ms and cap the complete operation
at another 9,750 ms, including authentication refresh, response handling and image
decoding. The HTTP request itself is capped at nine seconds. Timeout cancels the
operation, unlocks the form and preserves the draft. If text arrived while the
optional image is still decoding, that text is applied and the image is omitted;
late results cannot replace subsequent edits. Blob URLs are released on failure
or cancellation and transferred explicitly on success. Historical audits using
the former 20/35-second budgets remain archived; their coverage is not evidence
of the shorter deadline's current live success rate.

Complete previews and previews with a confirmed ambiguous price, name and image
can normally be reused from a process-local cache for ten minutes after retrieval.
In managed production mode or with local rolling admission enabled, a price supplied by Firecrawl can
instead be reused until its confirmed capture time plus 48 hours, less ten seconds.
For `cacheState: hit`, a valid non-future `cachedAt` supplies that capture time;
for `miss`, the request's start is used. A missing, invalid, future or unknown
cache timestamp retains the ten-minute lifetime, and a reported timestamp already
outside the limit is rejected. Prices obtained from the initial Cloudflare page
or local Cloudflare browser are reusable for up to 48 hours from the beginning
of the observation, less ten seconds; this bounds MonKado's own cache age but
cannot establish the age of an upstream merchant or CDN response. When such a
price has been in MonKado's cache for ten minutes, the same editable-price
advisory is added on cache hits. Ambiguous prices retain the ordinary lifetime.
Firecrawl's requested maximum age leaves
room for the ordinary cache and bounded transfer within the selected 48-hour
limit, provided the provider honors its cache metadata. Merchant prices may
change during this period. In adaptive mode, when the first page already provides a name and a
usable or explicitly ambiguous price, its free image path can take up to two
seconds before the browser or paid page fallback is used. This budget is reduced
when needed to reserve three seconds for an available final page provider. It
adds no paid request and does not extend the overall import deadline.
When adaptive mode supplies the price through the managed fallback, the form
keeps the editable value and displays an advisory that it may be up to 48 hours
old. The advisory is retained on cache hits and does not make an otherwise
complete preview ineligible for reuse.
The expiry does not slide on access. The cache holds at
most 128 entries and sixteen MiB of accounted payload, with least-recently-used
eviction; URL strings are not stored as keys. Each request still validates the
current public DNS set before lookup, preserves its exact query and returned URL,
and receives independent mutable response fields. In the adaptive cache,
normalized image bytes are stored without their base64 representation and are
encoded again on a cache hit; the same sixteen-MiB bound applies to the binary
payload. The ordinary cache is unchanged. The cache ignores fragments,
normalizes hostname casing and omits an explicit default port; path and query
remain exact because they can select different products or variants. Other partial previews and
provider errors are not cached. Concurrent requests for the same uncached URL share one
provider operation; a waiting caller can cancel independently, and an owner's
cancellation releases the key for another caller. Waiting, retrieval and image
processing stay inside each caller's existing eight-second budget. This reduces
duplicate provider requests without claiming to unblock a refused first access.

Duplicate structured product descriptions may use different publisher-local SKUs.
The parser can reconcile them using a matching GTIN with a valid GS1 check digit,
including GTIN-8/12/13/14 and exact decimal JSON numbers. The identifier may be on
the product or consistently identify every direct offer; missing or conflicting
offer identities never select an unchosen variant. Conflicting GTINs and ISBNs
remain distinct, and merged offer price disagreements remain unavailable. This
uses standard structured data only and performs no additional merchant requests.

The generic parser also recognizes named Schema.org entities connected to typed
offers, including books, recordings, services and events. It excludes catalogues,
organizations and page containers; ISBNs distinguish editions with the same title.
On a ProductGroup, passive HTML selection state (selected options, checked radios
and ARIA options) may resolve a unique structured size, colour or identifier. The
selected value must match structured variant attributes exactly after whitespace
and case normalization; conflicting or incomplete selections remain ambiguous.
Query-bearing links retain URL-based selection rather than guessing from a default
control. No price is inferred from position, a range minimum or arbitrary body text.
A uniquely owned main heading that matches its declared microdata product name
can identify an otherwise URL-less primary product among recommendation scopes.
Explicit URL identity still wins, conflicting identities remain unresolved and
equivalent declarations retain all their offers. Bare site-brand titles from
unloaded application shells do not prevent a rendered fallback's actual product
name from being used; explicit structured product names remain authoritative.
An exact query-bearing offer reference also selects a concrete offer nested inside
an AggregateOffer, with bounded traversal and all matching conflicts retained.
An unpriced selected offer cannot borrow a sibling's price or page metadata.
This applies equally to sizes and capacities: an unselected capacity keeps an empty
price, while an explicit URL or passive control may identify its exact offer.
For an in-stock aggregate range with no concrete child offers, bounded passive JSON
component state can bind one selected SKU to one enabled button's explicit EUR
amount. The state must belong to the article with the matching product heading;
the visible amount must fall within the declared range. Integer state amounts are
never assigned guessed units, and recommendation articles are excluded.
When there is no structured or page-level offer evidence, passive JSON data
attributes may provide an explicit formatted EUR price. The state must identify
the document's exact SKU and contain its sole matching product heading. Every
named product/variant in that state participates in the price decision: conflicting
or missing amounts remain unresolved. Original/VAT-exclusive prices and integer
amounts without explicit units are ignored. A URL can select a state variant only
through exact matching size, colour or identifier values, including `variant[color]`
style parameters; unknown, conflicting or malformed parameters are never guessed.
JSON size, document count, object count and nesting depth are bounded.
Firecrawl redirects from a product path to a storefront root produce no product
suggestion, even if the storefront returns HTTP 200.

Two independent, default-off local image experiments are available:
`WISH_IMPORT_LOCAL_CLOUDFLARE_IMAGES_ENABLED` permits an authenticated edge image
fetch after a pinned direct failure, with public DNS checked before/after in Go
and Workers, HTTPS, no edge redirects and a ten-MiB body limit.
`WISH_IMPORT_LOCAL_FIRECRAWL_IMAGES_ENABLED` permits a final `rawBase64` fetch of
the original photo after page fallback. That format is requested alone; reported
merchant status, MIME, initial/final public DNS, encoded/decoded limits and local
pixel normalization must all pass. It is not a screenshot. Previously failed
direct/edge image URLs can enter this final stage without repeating their direct
download. All images still become the same stripped WebP output.

Firecrawl page and image calls share the account admission clock, two-slot
concurrency limit and cooldown. Each call can consume free credits; there is no
paid-plan change, automatic purchase or unbounded retry. At most three candidates
per image phase fit inside the same total deadline. Both experiments require
their local parent provider and production startup rejects them.
Firecrawl explicitly requests the French market/languages for EUR previews and
keeps merchant TLS verification enabled. Generic product-price metadata aliases
and exact decimal commas in HTML metadata are supported; JSON-LD numbers remain
strict. Equivalent Product/ProductGroup declarations retain their variant rules,
so conflicting variants cannot be replaced with an arbitrary page metadata price.
Explicit block-page headings are not product suggestions.

Initial and reported final merchant URLs must pass complete public-DNS validation. Firecrawl
controls its internal renderer, IP choices, redirects and subresources; these are
not IP-pinned or individually inspected by MonKado. Jenn's explicit exception
applies only to local testing, not production qualification.

The final page provider receives one POST without transparent retries, capped at
six seconds inside the eight-second server preview. Page and image admission
waits consume that same deadline; requests that cannot fit return partial fields.
Canceled admission waiters do not reserve future starts. A shared cancellable
gate commits the 6.1-second pacing interval only when a request has a usable
transfer budget and can actually begin. Account cooldown and the two-request
in-flight limit still apply to both page and image calls.
If the primary document already supplied the name and exact price, a missing
image is requested as bounded original bytes before spending another provider
call on rendering the known product text. Missing text still prioritizes page
metadata; invalid image bytes still permit a page-based alternative within the
same overall deadline. No known primary name or price is overwritten.
HTML is capped at four MiB and the JSON envelope at six times that size
to allow JSON escaping. Each provider can contribute at most three image download
attempts. API starts are spaced by at least 6.1 seconds, with at most two in flight.
Account errors stop further calls; HTTP 429 respects `Retry-After` and pending
requests recheck that cooldown. No paid plan or automatic credit purchase is enabled.

See the [development relay boundary](../src/Tools.CloudflareWishImport/README.md),
including the explicit local-only IP-pinning exception. Production activation is
rejected. No production deployment is included in this change.

## Work and security bounds

The existing total timeout, direct redirect bound, DNS-pinned public-address checks,
decompressed byte bounds and image normalization remain enforced. HTML accepts at
most 4 MiB. Direct transport explicitly enables HTTP/2 negotiation alongside its
custom DNS-pinned dialer and verified TLS configuration, with HTTP/1.1 fallback.
A native TLS fixture checks both protocols, merchant authority, SNI and public-address
pinning without Internet access or production trust changes.

Transient GET failures allow at most two retries inside the same preview deadline. Permanent refusals, missing pages and oversized responses are not retried. No browser process, cookies, proxy, paid service, CAPTCHA solver or competitor code is introduced.

## Availability on mobile

Mobile and desktop previews use the same server-side provider. A product opening in the member's merchant app does not prove that its short link or product metadata is available to the server. Redirects remain bounded and independently validated; a missing short-link target cannot be reconstructed from its opaque identifier. Keep the original link and any manual draft when suggestions are absent. The frontend distinguishes an empty import from partially usable suggestions and offers an explicit link to open the source and copy the full product address.

CI uses deterministic synthetic documents and fake transport. Live merchant checks are explicit diagnostics outside CI and cannot guarantee permanent availability of third-party pages.


## Permanent managed providers

Jenn explicitly extended the managed-network exception to production on 27 October
2026 when requesting deployment with wish copying. `WISH_IMPORT_MANAGED_ENABLED`
selects the permanent provider mode. Its dedicated HTTPS Worker origin and random
32-byte hexadecimal secret must be configured only on the backend. The Worker
receives that secret as `IMPORT_AUTH`; `TEST_AUTH` remains a legacy local-preview
binding. Invalid origins, keys and dependent toggles fail startup. The loopback
local flags continue to be rejected in production and cannot be combined with the
permanent mode.

The Worker origin is limited to `monkado-wish-import.<account>.workers.dev` with no
credentials, port, path, query or fragment. The Go transport independently validates
and pins the provider's public DNS addresses, verifies its TLS identity, ignores
process proxies and refuses redirects. Its authenticated request body contains only
the public merchant URL, never member session data. Merchant public DNS is validated
before and after retrieval in Go and the Worker. Worker merchant IPs remain managed;
Firecrawl also manages its internal redirects and subresources. This remains an
explicit exception, not a claim of merchant-IP pinning.

The production pipeline is the same bounded Cloudflare-first flow tested locally,
with one Firecrawl fallback if configured, direct pinned image downloads followed
by optional Firecrawl original-image relay, and the ten-second form deadline. No
experimental browser, CAPTCHA solver, merchant-specific parser or subscription
upgrade is activated. The default configuration remains disabled until explicitly
provisioned. All normal deployment backup/approval/quality gates still apply.

The permanent relay client selects HTTP/1.1 before sending its request. Controlled
manual comparisons found upstream HTTP 429 responses over HTTP/2 for documents
that the same authenticated relay returned successfully over HTTP/1.1. This
selection does not retry a refusal or change the user agent, provider authority,
DNS pinning, public-address checks, TLS verification, redirects or deadlines.
Other merchant and provider transports retain their existing HTTP/2 support.
