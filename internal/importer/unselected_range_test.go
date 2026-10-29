package importer_test

import (
	"context"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenStartingPriceHasUnselectedSize_ReportsPriceAmbiguity(t *testing.T) {
	const schema = `<script type="application/ld+json">{"@type":"Product","name":"Pink rabbit","image":"/photo","isVariantOf":{"@type":"ProductGroup"},"offers":{"@type":"AggregateOffer","lowPrice":"19.90","priceCurrency":"EUR"}}</script>`
	const label = `<span>À partir de 19,90 €</span>`
	const size = `<select id="Size"><option value="" selected disabled>Choose</option><option value="small">Small</option><option value="large">Large</option></select>`
	for _, tc := range []struct {
		name, schema, label, size, query string
		ambiguous                        bool
	}{
		{"unselected size", schema, label, size, "", true},
		{"tracking query", schema, label, size, "?utm_source=share", true},
		{"unknown selection query", schema, label, size, "?size=small", false},
		{"selected size", schema, label, strings.Replace(size, `value="small"`, `value="small" selected`, 1), "", false},
		{"only one size", schema, label, strings.Replace(size, `<option value="large">Large</option>`, "", 1), "", false},
		{"unrelated select", schema, label, strings.Replace(size, `id="Size"`, `id="sort"`, 1), "", false},
		{"different displayed minimum", schema, strings.Replace(label, "19,90", "15,90", 1), size, "", false},
		{"ordinary displayed price", schema, strings.Replace(label, "À partir de ", "", 1), size, "", false},
		{"hidden starting price", schema, `<div hidden>` + label + `</div>`, size, "", false},
		{"price precedes starting label", schema, `<span>19,90 € à partir de</span>`, size, "", false},
		{"inverted range", strings.Replace(schema, `"lowPrice":"19.90"`, `"lowPrice":"19.90","highPrice":"10.00"`, 1), label, size, "", false},
		{"single aggregate still explicitly starts at this price", strings.Replace(schema, `"lowPrice":"19.90"`, `"lowPrice":"19.90","offerCount":1`, 1), label, size, "", true},
		{"not a variant group", strings.Replace(schema, `"ProductGroup"`, `"Product"`, 1), label, size, "", false},
		{"foreign currency", strings.Replace(schema, `"EUR"`, `"USD"`, 1), label, size, "", false},
		{"different heading", schema, label, size, "#different", false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product" + tc.query)
			heading := "Pink rabbit"
			if tc.name == "different heading" {
				heading = "Another product"
			}
			document := tc.schema + `<main><h1>` + heading + `</h1>` + tc.label + tc.size + `</main>`
			// Act
			result := importer.Extract([]byte(document), base)
			// Assert
			if result.PriceAmbiguous != tc.ambiguous {
				t.Fatalf("unexpected ambiguity %v", result.PriceAmbiguous)
			}
		})
	}
}

func TestPreview_WhenStartingPriceHasUnselectedSize_SkipsFallbackAndCachesDecision(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	page.get = func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		html := `<script type="application/ld+json">{"@type":"Product","name":"Notebook","image":"/photo","isVariantOf":{"@type":"ProductGroup"},"offers":{"@type":"AggregateOffer","lowPrice":"19.90","priceCurrency":"EUR"}}</script><main><h1>Notebook</h1><span>À partir de 19,90 €</span><select name="size"><option value="" selected disabled>Choose</option><option value="small">Small</option><option value="large">Large</option></select></main>`
		return importer.Extract([]byte(html), target), nil
	}
	fallbackCalls := 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		fallbackCalls++
		return importer.Metadata{}, nil
	}}
	browserCalls := 0
	client.RenderedSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		browserCalls++
		return importer.Metadata{}, nil
	}}

	// Act
	for range 2 {
		result, err := client.Preview(t.Context(), "https://merchant.test/product")
		if err != nil || result.Name == nil || result.Image == nil || result.Price != nil || len(result.Warnings) != 1 || result.Warnings[0] != "WISH_IMPORT_PRICE_UNAVAILABLE" {
			t.Fatalf("unselected starting price was not kept empty: %v; warnings %v", err, result.Warnings)
		}
	}
	// Assert
	if page.calls.Load() != 1 || fallbackCalls != 0 || browserCalls != 0 {
		t.Fatal("a known ambiguity caused unnecessary provider requests")
	}
}
