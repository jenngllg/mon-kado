package importer_test

import (
	"context"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenProductDetailsAvailable_ReturnsSafeEditableFields(t *testing.T) {
	for _, tc := range []struct{ name, markup, query, note, color, size string }{
		{"structured text", `<script type="application/ld+json">{"@type":"Product","name":"Pull","description":"<p>Coton &amp; lin.</p><p>Doux.</p><script>secret()<\/script>","color":"Vert","size":"M"}</script>`, "", "Coton & lin. Doux.", "Vert", "M"},
		{"open graph", `<title>Vase</title><meta property="og:description" content="Verre &amp; céramique">`, "", "Verre & céramique", "", ""},
		{"twitter", `<title>Vase</title><meta name="twitter:description" content="Fait main">`, "", "Fait main", "", ""},
		{"description fallback", `<title>Vase</title><meta name="description" content="Fait main">`, "", "Fait main", "", ""},
		{"microdata", `<article itemscope itemtype="https://schema.org/Product"><h1 itemprop="name">Pull</h1><p itemprop="description">Coton bio</p><meta itemprop="color" content="Bleu"><span itemprop="size">L</span></article>`, "", "Coton bio", "Bleu", "L"},
		{"rdfa", `<article vocab="https://schema.org/" typeof="Product"><h1 property="name">Pull</h1><p property="description">Coton bio</p><meta property="color" content="Bleu"><span property="size">L</span></article>`, "", "Coton bio", "Bleu", "L"},
		{"conflicting meta", `<title>Vase</title><meta property="og:description" content="A"><meta property="og:description" content="B">`, "", "", "", ""},
		{"selected variant", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Pull","hasVariant":[{"@type":"Product","url":"/product?v=M","color":"Bleu","size":"M","description":"Coton bleu"},{"@type":"Product","url":"/product?v=L","color":"Rouge","size":"L","description":"Coton rouge"}]}</script>`, "?v=L", "Coton rouge", "Rouge", "L"},
		{"unselected variants", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Pull","hasVariant":[{"@type":"Product","color":"Bleu","size":"M"},{"@type":"Product","color":"Bleu","size":"L"}]}</script><select><option selected>M</option></select>`, "", "", "Bleu", ""},
		{"missing variant attribute", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Pull","hasVariant":[{"@type":"Product","color":"Bleu"},{"@type":"Product"}]}</script>`, "", "", "", ""},
		{"size specification", `<script type="application/ld+json">{"@type":"Product","name":"Pull","size":{"@type":"SizeSpecification","name":"EU 42"}}</script>`, "", "", "", "EU 42"},
		{"explicit dimensions", `<script type="application/ld+json">{"@type":"Product","name":"Shelf","width":"76.5 cm","height":"76.5 cm","depth":"39 cm"}</script>`, "", "", "", "L 76.5 cm × H 76.5 cm × P 39 cm"},
		{"quantitative dimension", `<script type="application/ld+json">{"@type":"Product","name":"Vase","height":{"@type":"QuantitativeValue","value":18,"unitText":"cm"},"width":9}</script>`, "", "", "", "H 18 cm"},
		{"unitless dimensions ignored", `<script type="application/ld+json">{"@type":"Product","name":"Vase","height":18,"width":"9"}</script>`, "", "", "", ""},
		{"microdata dimensions", `<article itemscope itemtype="https://schema.org/Product"><h1 itemprop="name">Vase</h1><div itemprop="height" itemscope itemtype="https://schema.org/QuantitativeValue"><meta itemprop="value" content="18"><meta itemprop="unitText" content="cm"></div></article>`, "", "", "", "H 18 cm"},
		{"explicit size wins over dimensions", `<script type="application/ld+json">{"@type":"Product","name":"Pull","size":"M","height":"70 cm"}</script>`, "", "", "", "M"},
		{"multiple colors not selected", `<script type="application/ld+json">{"@type":"Product","name":"Pull","color":["Bleu","Rouge"]}</script>`, "", "", "", ""},
		{"unicode normalized", `<script type="application/ld+json">{"@type":"Product","name":"Pull","description":"Cafe\u0301","color":"e\u0301cru"}</script>`, "", "Café", "écru", ""},
		{"challenge ignored", `<title>Access denied</title><meta property="og:description" content="Blocked">`, "", "", "", ""},
		{"storefront description ignored", `<title>Shop</title><meta property="og:site_name" content="Shop"><meta name="description" content="Our shop and all our products">`, "", "", "", ""},
		{"oversized attributes omitted", `<script type="application/ld+json">{"@type":"Product","name":"Pull","color":"` + strings.Repeat("x", 101) + `"}</script>`, "", "", "", ""},
		{"description bounded", `<script type="application/ld+json">{"@type":"Product","name":"Pull","description":"` + strings.Repeat("🎁", 510) + `"}</script>`, "", strings.Repeat("🎁", 500), "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/product" + tc.query)
			// Act
			result := importer.Extract([]byte(tc.markup), base)
			// Assert
			for key, value := range map[string]struct {
				got  *string
				want string
			}{"note": {result.Note, tc.note}, "color": {result.Color, tc.color}, "size": {result.Size, tc.size}} {
				got := ""
				if value.got != nil {
					got = *value.got
				}
				if got != value.want {
					t.Fatalf("%s = %q, want %q", key, got, value.want)
				}
			}
		})
	}
}

func TestPreview_WhenCachedDetailsAreEdited_DoesNotChangeLaterSuggestions(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	base, _ := url.Parse("https://merchant.test/product")
	page.get = func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
		return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Notebook","description":"Paper","color":"Blue","size":"A5","image":"/photo","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base), ctx.Err()
	}
	// Act
	first, err := client.Preview(t.Context(), base.String())
	requireCompleteCachedPreview(t, first, err)
	if first.Note == nil || first.Color == nil || first.Size == nil {
		t.Fatal("details missing")
	}
	*first.Note, *first.Color, *first.Size = "Changed", "Changed", "Changed"
	second, err := client.Preview(t.Context(), base.String())
	// Assert
	requireCompleteCachedPreview(t, second, err)
	if *second.Note != "Paper" || *second.Color != "Blue" || *second.Size != "A5" || page.calls.Load() != 1 {
		t.Fatal("cached details mutated or page refetched")
	}
}

func TestPreview_WhenFallbackSuggestsAnUnchosenSize_KeepsVariantAmbiguity(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	page.get = func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"ProductGroup","name":"Notebook","description":"Primary description","image":"/photo","hasVariant":[{"@type":"Product","size":"A4","offers":{"price":"12.30","priceCurrency":"EUR"}},{"@type":"Product","size":"A5"}]}</script>`), target), nil
	}
	client.FallbackSource = fallbackPage{get: func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Notebook","description":"Other description","size":"A5","color":"Blue","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), target), nil
	}}
	// Act
	result, err := client.Preview(t.Context(), "https://merchant.test/product")
	// Assert
	if err != nil || result.Size != nil || result.Note == nil || *result.Note != "Primary description" {
		t.Fatal("fallback guessed an unchosen variant or replaced available details")
	}
}
