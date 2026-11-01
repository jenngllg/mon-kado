package importer_test

import (
	"context"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenDisplayedProductPriceConflicts_DoesNotGuess(t *testing.T) {
	const product = `<script type="application/ld+json">{"@type":"Product","name":"Cleansing balm","offers":{"@type":"Offer","price":"10","priceCurrency":"EUR"}}</script>`
	const marked = `<span data-element="price">32 €</span>`
	for _, tc := range []struct {
		name, markup string
		conflict     bool
	}{
		{"different marked price", `<main><h1>Cleansing balm</h1>` + marked + `</main>`, true},
		{"different microdata price", `<main><h1>Cleansing balm</h1><span itemprop="price">32,00 €</span></main>`, true},
		{"equal amounts", `<main><h1>Cleansing balm</h1><span data-element="price">10,00 €</span></main>`, false},
		{"unrelated recommendation", `<main><h1>Cleansing balm</h1><article><h2>Other</h2>` + marked + `</article></main>`, false},
		{"linked recommendation", `<main><h1>Cleansing balm</h1><a href="/other">` + marked + `</a></main>`, false},
		{"another heading", `<main><h1>Another product</h1>` + marked + `</main>`, false},
		{"two headings", `<main><h1>Cleansing balm</h1><h1>Another</h1>` + marked + `</main>`, false},
		{"outside product scope", `<main><h1>Cleansing balm</h1></main><aside>` + marked + `</aside>`, false},
		{"unmarked text", `<main><h1>Cleansing balm</h1><span>32 €</span></main>`, false},
		{"hidden price", `<main><h1>Cleansing balm</h1><div hidden>` + marked + `</div></main>`, false},
		{"struck through price", `<main><h1>Cleansing balm</h1><del>` + marked + `</del></main>`, false},
		{"non euro", `<main><h1>Cleansing balm</h1>` + strings.Replace(marked, "32 €", "32 USD", 1) + `</main>`, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product")
			// Act
			result := importer.Extract([]byte(product+tc.markup), base)
			// Assert
			if tc.conflict {
				if result.Price != nil || !result.PriceAmbiguous {
					t.Fatalf("conflicting amounts must remain unset: %v", result.Price)
				}
			} else if result.Price == nil || result.Price.String() != "10" || result.PriceAmbiguous {
				t.Fatalf("unrelated markup changed the price: %v", result.Price)
			}
		})
	}
}

func TestPreview_WhenDisplayedProductPriceConflicts_DoesNotAskFallbackToGuess(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	page.get = func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		document := `<script type="application/ld+json">{"@type":"Product","name":"Notebook","image":"/photo","offers":{"price":"10","priceCurrency":"EUR"}}</script><main><h1>Notebook</h1><span data-element="price">32 €</span></main>`
		return importer.Extract([]byte(document), target), nil
	}
	calls := 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		calls++
		return importer.Metadata{}, nil
	}}
	client.RenderedSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		calls++
		return importer.Metadata{}, nil
	}}
	// Act
	for range 2 {
		result, err := client.Preview(t.Context(), "https://merchant.test/product")
		if err != nil || result.Name == nil || result.Image == nil || result.Price != nil || len(result.Warnings) != 1 || result.Warnings[0] != "WISH_IMPORT_PRICE_UNAVAILABLE" {
			t.Fatalf("conflicting price was not kept empty: %v", err)
		}
	}
	// Assert
	if calls != 0 || page.calls.Load() != 1 {
		t.Fatal("a known conflict caused unnecessary provider requests")
	}
}
