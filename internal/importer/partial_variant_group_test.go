package importer_test

import (
	"context"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenProductAndVariantGroupConflict_RecoversOnlyIdentifiedSharedFields(t *testing.T) {
	// Arrange
	const structured = `[{"@type":"Product","name":"Pillow","url":"/product","sku":"A","gtin13":"4006381333931","offers":[{"gtin13":"4006381333931","price":"20","priceCurrency":"EUR"},{"gtin13":"9780306406157","price":"30","priceCurrency":"EUR"}]},{"@type":"ProductGroup","name":"Pillow","url":"/product","hasVariant":[{"sku":"A","gtin13":"4006381333931","offers":{"price":"20","priceCurrency":"EUR"}},{"sku":"B","gtin13":"9780306406157","offers":{"price":"30","priceCurrency":"EUR"}}]}]`
	for _, tc := range []struct {
		name, schema, heading, query string
		want                         bool
	}{
		{"known group membership", structured, "Pillow", "", true},
		{"tracking parameters do not select a variant", structured, "Pillow", "?utm_source=share&gclid=test", true},
		{"tracking mixed with selection remains unresolved", structured, "Pillow", "?utm_source=share&variant=other", false},
		{"malformed query remains unresolved", structured, "Pillow", "?utm_source=share;variant=other", false},
		{"unknown membership", strings.Replace(structured, `"sku":"A"`, `"sku":"C"`, 1), "Pillow", "", false},
		{"duplicate member sku", strings.Replace(structured, `"sku":"B"`, `"sku":"A"`, 1), "Pillow", "", false},
		{"different product name", strings.Replace(structured, `"name":"Pillow"`, `"name":"Other"`, 1), "Pillow", "", false},
		{"different heading", structured, "Different product", "", false},
		{"query selection is not guessed", structured, "Pillow", "?variant=other", false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			markup := `<h1>` + tc.heading + `</h1><meta property="og:image" content="/photo.png"><meta property="og:price:amount" content="20"><meta property="og:price:currency" content="EUR"><script type="application/ld+json">` + tc.schema + `</script>`
			base, _ := url.Parse("https://shop.test/product" + tc.query)

			// Act
			result := importer.Extract([]byte(markup), base)

			// Assert
			if result.Price != nil {
				t.Fatalf("unselected variant supplied price %s", result.Price.String())
			}
			if (result.Name != nil) != tc.want || (result.Image != nil) != tc.want {
				t.Fatalf("identity recovery disagreed: name %v, image %v", result.Name, result.Image)
			}
			if tc.want && !result.PriceAmbiguous {
				t.Fatal("unselected variant ambiguity was lost")
			}
		})
	}
}

func TestPreview_WhenUnselectedOffersHaveDifferentPrices_SkipsProvidersAndCachesDecision(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	page.get = func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		html := `<script type="application/ld+json">{"@type":"Product","name":"Headphones","image":"/photo","offers":[{"price":"149","priceCurrency":"EUR"},{"price":"169","priceCurrency":"EUR"}]}</script>`
		return importer.Extract([]byte(html), target), nil
	}
	calls := 0
	client.RenderedSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) { calls++; return importer.Metadata{}, nil }}
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) { calls++; return importer.Metadata{}, nil }}
	// Act
	for range 2 {
		result, err := client.Preview(t.Context(), "https://merchant.test/product")
		if err != nil || result.Name == nil || result.Image == nil || result.Price != nil || len(result.Warnings) != 1 || result.Warnings[0] != "WISH_IMPORT_PRICE_UNAVAILABLE" {
			t.Fatalf("unselected offers were not left empty: %v", err)
		}
	}
	// Assert
	if calls != 0 || page.calls.Load() != 1 {
		t.Fatal("a known offer conflict caused unnecessary provider requests")
	}
}

func TestExtract_WhenVariantPricesDiffer_RecordsAmbiguityWithoutGuessing(t *testing.T) {
	// Arrange
	const group = `{"@type":"ProductGroup","name":"Gift","url":"/product","hasVariant":[{"offers":{"price":"20","priceCurrency":"EUR"}},{"offers":{"price":"30","priceCurrency":"EUR"}}]}`
	for _, tc := range []struct {
		name, schema, query, price string
		ambiguous, unsupported     bool
	}{
		{"different variant prices", group, "", "", true, false},
		{"tracked variant prices remain ambiguous", group, "?utm_source=share&fbclid=abc", "", true, false},
		{"tracking cannot hide unknown selection", group, "?utm_source=share&variant=unknown", "", false, false},
		{"equal variant prices", strings.Replace(group, `"price":"30"`, `"price":"20"`, 1), "", "20", false, false},
		{"missing variant price", strings.Replace(group, `"price":"30"`, `"price":""`, 1), "", "", false, false},
		{"unsupported currency", strings.Replace(group, `"priceCurrency":"EUR"`, `"priceCurrency":"USD"`, 1), "", "", false, true},
		{"unknown query selection", group, "?variant=unknown", "", false, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			base, _ := url.Parse("https://shop.test/product" + tc.query)

			// Act
			result := importer.Extract([]byte(`<script type="application/ld+json">`+tc.schema+`</script>`), base)

			// Assert
			price := ""
			if result.Price != nil {
				price = result.Price.String()
			}
			if price != tc.price || result.PriceAmbiguous != tc.ambiguous || result.UnsupportedCurrency != tc.unsupported {
				t.Fatalf("price %q, ambiguous %t, unsupported %t", price, result.PriceAmbiguous, result.UnsupportedCurrency)
			}
		})
	}
}

func TestExtract_WhenProductHasSeveralOffers_RecordsOnlyUnselectedPriceConflict(t *testing.T) {
	const product = `{"@type":"Product","name":"Headphones","url":"/product","offers":[{"@type":"Offer","url":"/product?model=basic","price":"149","priceCurrency":"EUR"},{"@type":"Offer","url":"/product?model=wireless","price":"169","priceCurrency":"EUR"}]}`
	for _, tc := range []struct {
		name, schema, query, price string
		ambiguous                  bool
	}{
		{"unselected offers", product, "", "", true},
		{"tracking only", product, "?utm_source=share", "", true},
		{"selected offer", product, "?model=basic", "149", false},
		{"unknown selected offer", product, "?model=unknown", "", false},
		{"matching prices", strings.Replace(product, `"price":"169"`, `"price":"149.00"`, 1), "", "149", false},
		{"missing price", strings.Replace(product, `"price":"169"`, `"price":""`, 1), "", "", false},
		{"foreign offer", strings.Replace(product, `"priceCurrency":"EUR"`, `"priceCurrency":"USD"`, 1), "", "", false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/product" + tc.query)
			// Act
			result := importer.Extract([]byte(`<script type="application/ld+json">`+tc.schema+`</script>`), base)
			// Assert
			got := ""
			if result.Price != nil {
				got = result.Price.String()
			}
			if got != tc.price || result.PriceAmbiguous != tc.ambiguous {
				t.Fatalf("price %q, ambiguous %t", got, result.PriceAmbiguous)
			}
		})
	}
}
