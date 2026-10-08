# Wish link import (#988)

The existing authenticated preview endpoint remains unchanged. It returns suggestions only: no wish or image is persisted until the member submits the form.

## Merchant support

- Amazon product markup is read on supported marketplace hosts, independently of ASIN or product path. Primary offer prices take precedence over accessibility labels containing savings. Conflicting amounts, non-euro prices, challenges and storefront logos are not substituted for product data.
- FNAC supports both structured product metadata and the current main-product markup. Membership discounts, crossed-out prices and recommendations are excluded.
- Generic JSON-LD and schema.org microdata select an explicitly matching product/offer URL or SKU, never a cheapest or first ambiguous variant.
- Sephora France optionally uses its own anonymous public product index. Exact SKUs or the master product's explicitly declared default variant are required; fuzzy suggestions are rejected.

## Optional public catalogue

`UrlImportCatalog__Enabled` defaults to `false`. To enable it, configure `UrlImportCatalog__Enabled=true` and `UrlImportCatalog__IndexKey` with the public storefront search-index identifier (`key_…`). This is not an administrative credential. An enabled but invalid configuration fails startup validation. Without activation, ordinary passive HTML extraction remains in use.

No production activation or deployment is included in this correction. The public index is a third-party dependency and may change or refuse requests; the member can always complete the wish manually.

## Work and security bounds

The existing total timeout, redirect bound, DNS-pinned public-address checks, decompressed byte bounds and image normalization remain enforced. HTML accepts at most 4 MiB; catalogue JSON at most 256 KiB. Transport negotiates HTTP/2 with HTTP/1.1 fallback. No browser process, cookies, proxy, paid service, CAPTCHA solver, automatic retry or competitor code is introduced.

CI uses deterministic synthetic documents and fake transport. Live merchant checks are explicit diagnostics outside CI and cannot guarantee permanent availability of third-party pages.
