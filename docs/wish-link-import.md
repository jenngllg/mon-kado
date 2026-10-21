# Wish link import (#988)

The existing authenticated preview endpoint remains unchanged. It returns suggestions only: no wish or image is persisted until the member submits the form.

## Merchant support

- Amazon product markup is read on supported marketplace hosts, independently of ASIN or product path. Primary offer prices take precedence over accessibility labels containing savings. Conflicting amounts, non-euro prices, challenges and storefront logos are not substituted for product data.
- FNAC supports both structured product metadata and the current main-product markup. Membership discounts, crossed-out prices and recommendations are excluded.
- Generic JSON-LD and schema.org microdata select an explicitly matching product/offer URL or SKU, never a cheapest or first ambiguous variant.
- Sephora France optionally uses its own anonymous public product index. Exact SKUs or the master product's explicitly declared default variant are required; fuzzy suggestions are rejected.

## Optional public catalogue

`WISH_IMPORT_CATALOG_ENABLED` defaults to `false`. To enable it, configure `WISH_IMPORT_CATALOG_ENABLED=true` and `WISH_IMPORT_CATALOG_INDEX_KEY` with the public storefront search-index identifier (`key_…`). This is not an administrative credential. An enabled but invalid configuration fails startup validation. Without activation, ordinary passive HTML extraction remains in use.

No production activation or deployment is included in this correction. The public index is a third-party dependency and may change or refuse requests; the member can always complete the wish manually.

## Work and security bounds

The existing total timeout, redirect bound, DNS-pinned public-address checks, decompressed byte bounds and image normalization remain enforced. HTML accepts at most 4 MiB; catalogue JSON at most 256 KiB. Transport explicitly enables HTTP/2 negotiation alongside its custom DNS-pinned dialer and verified TLS configuration, with HTTP/1.1 fallback. A native TLS fixture checks both protocols, merchant authority, SNI and public-address pinning without Internet access or production trust changes.

Transient GET failures allow at most two retries inside the same preview deadline. Permanent refusals, missing pages and oversized responses are not retried. No browser process, cookies, proxy, paid service, CAPTCHA solver or competitor code is introduced.

## Availability on mobile

Mobile and desktop previews use the same server-side provider. A product opening in the member's merchant app does not prove that its short link or product metadata is available to the server. Redirects remain bounded and independently validated; a missing short-link target cannot be reconstructed from its opaque identifier. Keep the original link and any manual draft when suggestions are absent. The frontend distinguishes an empty import from partially usable suggestions and offers an explicit link to open the source and copy the full product address.

CI uses deterministic synthetic documents and fake transport. Live merchant checks are explicit diagnostics outside CI and cannot guarantee permanent availability of third-party pages.
