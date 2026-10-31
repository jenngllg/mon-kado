# Product-link import: live merchant evidence

This directory records a local audit of 30 real product URLs on 31 October 2026. It uses the real Go `importer.Client.Preview` through `src/Tools.WishImportAudit`, with the default direct public-only connector and **no Cloudflare or Firecrawl provider configured**.

**Final result: 8 complete imports, 2 partial imports, 20 unavailable imports. This is not a claim of support for all 30 merchants.** A complete import here means a matching product name, description, normalized image and EUR price. The eight complete prices were also checked against structured and visible product prices in a separate current HTML download.

The final pass ran from **2026-10-31 11:47:28.497 UTC** to **2026-10-31 11:47:46.673 UTC**. Every import had the existing eight-second budget. This command only requested previews; it did not save wishes, create orders, authenticate to merchants, bypass challenges or change production configuration.

## Final 30-merchant matrix

Each merchant links to the tested product. `Description` maps to the existing wish `note` field. IKEA supplies exact structured physical dimensions, imported into the existing size field. Clothing/shoe sizes remain empty where the link does not unambiguously select one. Quantity stayed at the safe default of 1 rather than being inferred from page recommendations.

| Merchant / tested product | Result | Name | Description | Price | Image | Color | Size / dimensions | Limitation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [Amazon](https://www.amazon.fr/dp/B0DHSDNPHC) | Partial | Yes | Yes | Empty | [WebP](01.webp) | Empty | Empty | Price absent |
| [Maisons du Monde](https://www.maisonsdumonde.com/FR/fr/p/vase-en-verre-teinte-vert-h18-241834.htm) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Decathlon](https://www.decathlon.fr/p/gourde-900-isotherme-inox-0-8l-bouchon-a-ouverture-rapide-pour-la-randonnee/301275/c328m8971230) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Cdiscount](https://www.cdiscount.com/juniors/jeux-de-construction/lego-city-60454-le-camping-car-d-aventures-jeu-d/f-12083-lego60454.html) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Fnac](https://www.fnac.com/LEGO-Icons-10281-Bonsai/a15233617/w-4) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Darty](https://www.darty.com/nav/achat/accessoires/casque_ecouteurs/casque_arceau/marshall_major_v_black.html) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [IKEA](https://www.ikea.com/fr/fr/p/kallax-etagere-blanc-20275814/) | Complete | Yes | Yes | 34.99 EUR | [WebP](07.webp) | blanc | L 76.5 cm × H 76.5 cm × P 39 cm | None |
| [LEGO](https://www.lego.com/fr-fr/product/bonsai-tree-10281) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Boulanger](https://www.boulanger.com/ref/1204873) | Complete | Yes | Yes | 149.99 EUR | [WebP](09.webp) | Empty | Empty | None |
| [Cultura](https://www.cultura.com/p-azul-3504871.html) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Sephora](https://www.sephora.fr/p/335957.html) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Nocibé](https://www.nocibe.fr/fr/p/3000056790?variant=606251) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [La Redoute](https://www.laredoute.fr/ppdp/prod-602685461.aspx) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [L'Occitane](https://fr.loccitane.com/creme-mains-karite-01MA150K26.html) | Unavailable | No | No | Empty | No | Empty | Empty | Redirect limit |
| [Rituals](https://www.rituals.com/fr-fr/the-ritual-of-sakura---large-gift-set-2024-1118461.html) | Partial | Yes | Yes | Empty | [WebP](15.webp) | Empty | Empty | Conflicting prices |
| [Lush](https://www.lush.com/fr/fr/p/sleepy-body-lotion) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Nike](https://www.nike.com/fr/t/chaussure-nike-air-force-1-07-pour-homme-n8juM1H2/CW2288-111) | Complete | Yes | Yes | 119.99 EUR | [WebP](17.webp) | Blanc/Blanc | Empty | None |
| [Adidas](https://www.adidas.fr/chaussure-y-3-stan-smith/KI3464.html) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Zalando](https://www.zalando.fr/adidas-originals-stan-smith-unisex-baskets-basses-core-black-ad115o0w8-q11.html) | Unavailable | No | No | Empty | No | Empty | Empty | 8 s timeout |
| [E.Leclerc](https://www.e.leclerc/fp/nike-air-force-1-triple-white-blanc-0194500874862) | Complete | Yes | Yes | 102.95 EUR | [WebP](20.webp) | Empty | Empty | None |
| [Starcow Paris](https://www.starcowparis.com/products/nike-air-force-1-07-white) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 429 |
| [Sony](https://www.sony.fr/store/product/wh1000xm6b.ce7/1000X-SERIES-Casque-sans-fil-%25C3%25A0-r%25C3%25A9duction-de-bruit-WH-1000XM6) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Materiel.net](https://www.materiel.net/produit/202508270041.html) | Complete | Yes | Yes | 89.95 EUR | [WebP](23.webp) | Empty | Empty | None |
| [Bose](https://www.bose.fr/p/headphones/quietcomfort-acoustic-noise-cancelling-headphones/QC-HEADPHONEARN.html?dwvar_QC-HEADPHONEARN_color=WHITE+SMOKE) | Complete | Yes | Yes | 249.95 EUR | [WebP](24.webp) | Empty | Empty | None |
| [LDLC](https://www.ldlc.com/fiche/PB00500370.html) | Complete | Yes | Yes | 119.95 EUR | [WebP](25.webp) | Empty | Empty | None |
| [King Jouet](https://www.king-jouet.com/jeu-jouet/jeux-societes/jeux-ambiance/ref-728250-dobble-classique.htm) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [JouéClub](https://www.joueclub.fr/jeux-de-societes/dobble-classique-3558380052845.html) | Unavailable | No | No | Empty | No | Empty | Empty | Empty HTML |
| [Nature & Découvertes](https://www.natureetdecouvertes.com/huiles-essentielles-diffuseur/diffuseur-huiles-essentielles/par-chaleur-douce/diffuseur-d-huiles-essentielles-warm-15227440) | Complete | Yes | Yes | 34.95 EUR | [WebP](28.webp) | Empty | Empty | None |
| [Leroy Merlin](https://www.leroymerlin.fr/produits/lampe-led-koze-verre-blanc-h-16-5cm-inspire-89736652.html) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |
| [Lacoste](https://www.lacoste.com/fr/lacoste/homme/vetements/polos/manches-courtes/L1212-00.html?color=001) | Unavailable | No | No | Empty | No | Empty | Empty | HTTP 403 |

The `image` property in `results.json` is intentionally null because normalized bytes are saved as linked WebP files; `imageFile` and `imageSha256` identify those exact bytes. Images are extracted product pictures, not screenshots of the application. UI screenshots are included in the companion frontend pull request.

## Price verification and correction

The first pass, retained in [initial-results.json](initial-results.json), reported nine results containing a name, price and image. A subsequent HTML check exposed one real error: Rituals declared **39.92 EUR in JSON-LD** while the same page displayed an active product price of **49.90 EUR**. That result was not counted as a validated complete import.

A regression test reproduced the mismatch before the generic conflict detector was corrected. The final pass now keeps Rituals' matching name, description and image, leaves its price empty and returns `WISH_IMPORT_PRICE_UNAVAILABLE`. It does not guess between contradictory merchant values. The fix handles generic price markup and accessibility duplicates rather than a Rituals hostname selector.

| Merchant | Final price (EUR) | Evidence from the current downloaded HTML |
| --- | --- | --- |
| IKEA | 34.99 | Active dated price specification and visible 34.99; the old 39.99 is separate. |
| Boulanger | 149.99 | Product Offer and visible product price both 149.99. |
| Nike | 119.99 | Offers for the linked white color and visible product price all 119.99; no size guessed. |
| E.Leclerc | 102.95 | One concrete Offer and equal aggregate bounds; visible product price 102.95. |
| Materiel.net | 89.95 | Active Offer and visible promotion 89.95; old 109.95 and 30-day minimum 82.46 are distinct. |
| Bose | 249.95 | The WHITE SMOKE variant Offer and visible selected Blanc Nuage price both 249.95. |
| LDLC | 119.95 | Linked Graphite Offer and visible Graphite price 119.95; another gray variant is 129.95. |
| Nature & Decouvertes | 34.95 | Product Offer and visible price beside reference 15227440 both 34.95. |

Amazon returns the LEGO 72031 name, description and product picture. Its selected product's price block was empty, with no product Offer JSON-LD or price metadata. Recommendation prices were excluded, so its price correctly remains empty. Rituals is the other partial result because its own price sources disagree.

Across the first and final passes, all ten returned product names were identical. The eight complete prices were unchanged. All ten final image hashes match the first pass. An intermediate rerun returned different Amazon bytes depicting the same LEGO product, which was checked visually.

## Why 20 merchants remained unavailable

The separate secure diagnostic pass is recorded in [diagnostics.json](diagnostics.json) and explained in [DIAGNOSTICS.md](DIAGNOSTICS.md). It found 16 HTTP 403 responses, one HTTP 429 response, one redirect-limit failure, one eight-second timeout, and one empty successful HTML response.

For failures whose public API hid the status, the diagnostic made a second request using the unchanged validated public-only transport. Its HTTP status is explicitly a second observation and can differ from the original response. HTTP 403 alone does not identify a particular bot-protection vendor or mechanism. No explicit TLS or DNS failure was observed in this pass.

Maisons du Monde, Decathlon and Cdiscount are among these unavailable results. Their product pages were discoverable through web research, but this does not establish access from the application's direct server connector. Optional rendering providers were not configured or tested and cannot be credited with recovering these pages.

## Artifacts and reproduction

- [results.json](results.json): final raw preview results, UTC timestamps, elapsed time, warnings and image hashes.
- [initial-results.json](initial-results.json): first pass before the observed Rituals correction.
- [candidates.json](candidates.json): 30 precise input URLs. Research-only prices can be cached or stale; they are not the validation oracle.
- [diagnostics.json](diagnostics.json): safe transport errors/statuses, bounded metadata, structured-price fields, visible-price excerpts and private HTML hashes.
- Ten linked WebP files: normalized public product images from the final pass.

From the repository root, with Go installed:

```powershell
go build -o .local/wish-import-audit.exe ./src/Tools.WishImportAudit
.\.local\wish-import-audit.exe -live -input docs/testing/product-link-import/candidates.json -output .local/merchant-audit-new-run
```

The output directory must be new. Results depend on merchant availability, current prices and request origin. The command never reads credentials or enables optional providers. Raw merchant HTML remains under the private ignored `.local` directory and is not committed.

Standard extraction references: [Open Graph](https://ogp.me/), [Schema.org Product](https://schema.org/Product), [Schema.org Offer](https://schema.org/Offer). Public-only request controls follow the concerns described in the [OWASP SSRF prevention guidance](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html).

## Exact physical dimensions

The final live pass imports IKEA KALLAX's explicit structured dimensions into the existing size field as `L 76.5 cm × H 76.5 cm × P 39 cm`. The retained current HTML declares `width: "76.5 cm"`, `height: "76.5 cm"` and `depth: "39 cm"`. Its product name uses the rounded marketing dimensions `77x77 cm`; the importer preserves the exact structured measurements instead of deriving measurements from that rounded name. This is physical product dimension data, not an inferred clothing size or selected shoe variant. The other 29 size fields stayed empty.
