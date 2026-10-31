# Secure merchant diagnostics

A separate local diagnostic on 31 October 2026 inspected the same 30 URLs through the application's unchanged `Client.Download` connector. The complete structured record is [diagnostics.json](diagnostics.json). It never used merchant credentials, process proxies, private addresses, browser impersonation or external rendering providers.

For unsuccessful downloads, the public error intentionally hides the upstream HTTP status. The diagnostic therefore made one additional request through the same DNS-validated, IP-pinned transport and recorded its status separately. These second observations are not falsely attributed to the first request.

| Observation for the 20 initially unavailable merchants | Count | Merchants |
| --- | --- | --- |
| HTTP 403 on the second secure request | 16 | Maisons du Monde, Decathlon, Cdiscount, Fnac, Darty, LEGO, Cultura, Sephora, Nocibe, La Redoute, Lush, Adidas, Sony, King Jouet, Leroy Merlin, Lacoste |
| HTTP 429 on the second secure request | 1 | Starcow Paris |
| Redirect limit; final diagnostic status still 302 | 1 | L'Occitane |
| Eight-second deadline exceeded on both requests | 1 | Zalando |
| Successful 2xx download containing empty text/html | 1 | JoueClub |

HTTP refusal or throttling accounts for 17 of those 20 results. No explicit DNS or TLS error was recorded. A 403 status does not prove a specific challenge implementation or bot-protection vendor.

## Price correctness

Eight complete imports have equal structured and visible product prices in their respective downloaded HTML: IKEA 34.99, Boulanger 149.99, Nike 119.99, E.Leclerc 102.95, Materiel.net 89.95, Bose 249.95, LDLC 119.95, and Nature & Decouvertes 34.95 EUR. The matching Offer/price-specification fields and bounded visible contexts are retained in `diagnostics.json` under each merchant. Old prices, installment amounts, recommendations and other variants are distinct from the selected product price.

The initial Rituals result was a genuine conflict: JSON-LD declared 39.92 EUR, while active product-price markup displayed 49.90 EUR. Its product heading was split between a pre-title (`The Ritual of Sakura`) and an h1 (`Gift Set L`). The active amount used a CSS price container and an accessibility-hidden duplicate span, with no visual strikethrough. The corrected final importer retains product details and image but leaves the contradictory price unset, as recorded in [results.json](results.json).

Amazon had no product Offer or price metadata, and the selected ASIN B0DHSDNPHC's core product-price block was empty. Unrelated recommendation prices were present and excluded. Its missing-price warning is expected for this response.

## Evidence scope

The JSON keeps public product URLs, UTC observation times, safe error text, final public URLs, content media/size, SHA256, bounded relevant meta tags, selected JSON-LD fields and visible-price excerpts. `privateHtmlFile` identifies an ignored local artifact under `.local/merchant-diagnostics-html/`; those HTML files are deliberately not committed. Their hashes allow matching retained local evidence.

A temporary diagnostic test called the existing secure private request method to observe upstream status without changing the production API or transport. It was removed from `internal/importer` after execution. A local helper source copy remains ignored in `.local/merchant-diagnostic-helper.go.txt`.

This diagnostic explains limitations observed on the test date. It does not establish permanent merchant support or recovery through a provider that was not configured.
