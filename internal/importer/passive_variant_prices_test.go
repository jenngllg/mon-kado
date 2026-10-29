package importer_test

import (
	"context"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenSinglePassiveVariantHasDisplayedPrice_RequiresMatchingEvidence(t *testing.T) {
	const variant = `{"id":"one","product":{"title":"Glass cooking dish","url":"/product"},"price":{"amount":12.9,"currencyCode":"EUR"}}`
	const price = `<span data-price="1290">12,90 €</span>`
	for _, tc := range []struct {
		name, variants, displayed, query, extra, expected string
	}{
		{"single matching variant", variant, price, "", "", "12.9"},
		{"tracking query", variant, price, "?utm_source=link", "", "12.9"},
		{"duplicate same variant", variant + `,` + variant, price, "", "", "12.9"},
		{"selected query", variant, price, "?size=large", "", ""},
		{"two variants", variant + `,` + strings.Replace(variant, `"one"`, `"two"`, 1), price, "", "", ""},
		{"conflicting duplicate", variant + `,` + strings.Replace(variant, `12.9`, `19.9`, 1), price, "", "", ""},
		{"different product URL", strings.Replace(variant, `/product`, `/other`, 1), price, "", "", ""},
		{"different product name", strings.Replace(variant, `Glass cooking dish`, `Another product`, 1), price, "", "", ""},
		{"no visible price", variant, ``, "", "", ""},
		{"different visible price", variant, strings.Replace(price, "12,90", "19,90", 1), "", "", ""},
		{"hidden visible price", variant, `<div hidden>` + price + `</div>`, "", "", ""},
		{"old visible price", variant, `<del>` + price + `</del>`, "", "", ""},
		{"foreign currency", strings.Replace(variant, `EUR`, `USD`, 1), price, "", "", ""},
		{"zero price", strings.Replace(variant, `12.9`, `0`, 1), `<span data-price="0">0 €</span>`, "", "", ""},
		{"two visible sizes", variant, price, "", `<input type="radio" name="size" value="s"><input type="radio" name="size" value="l">`, ""},
		{"one visible size", variant, price, "", `<input type="radio" name="size" value="s">`, "12.9"},
		{"unrelated heading", variant, price, "", "heading-other", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product" + tc.query)
			heading, extra := "Glass cooking dish", tc.extra
			if extra == "heading-other" {
				heading, extra = "Another product", ""
			}
			document := `<title>Glass cooking dish | Shop</title><script>window.state={"productVariants":[` + tc.variants + `]};</script><main><h1>` + heading + `</h1>` + tc.displayed + extra + `</main>`
			// Act
			result := importer.Extract([]byte(document), base)
			// Assert
			if tc.expected == "" && result.Price != nil || tc.expected != "" && (result.Price == nil || result.Price.String() != tc.expected) {
				t.Fatalf("unexpected recovered price: %v", result.Price)
			}
		})
	}
}

func TestPreview_WhenSinglePassiveVariantSuppliesPrice_DoesNotUseFallback(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	page.get = func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		document := `<title>Notebook gift</title><meta property="og:image" content="/photo"><script>window.state={"productVariants":[{"id":"one","product":{"title":"Notebook gift","url":"/product"},"price":{"amount":12.9,"currencyCode":"EUR"}}]};</script><main><h1>Notebook gift</h1><span data-price="1290">12,90 €</span></main>`
		return importer.Extract([]byte(document), target), nil
	}
	calls := 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) { calls++; return importer.Metadata{}, nil }}
	// Act
	result, err := client.Preview(t.Context(), "https://merchant.test/product")
	// Assert
	if err != nil || result.Price == nil || result.Price.String() != "12.9" || result.Image == nil || calls != 0 {
		t.Fatalf("passive price did not avoid fallback: %v", err)
	}
}
