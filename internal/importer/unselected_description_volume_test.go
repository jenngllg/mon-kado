package importer_test

import (
	"context"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenDescriptionListsAlternativeVolumes_LeavesUnselectedPriceEmpty(t *testing.T) {
	const description = `<div itemprop="description"><p>Volume : 150 ml ou 250 ml</p></div>`
	for _, tc := range []struct {
		name, description, heading, query string
		ambiguous                         bool
	}{
		{"two volumes", description, "Reusable bottle", "", true},
		{"tracking parameters", description, "Reusable bottle", "?utm_source=share", true},
		{"two units", strings.Replace(description, "150 ml ou 250 ml", "15 cl ou 0,25 l", 1), "Reusable bottle", "", true},
		{"same volume in different units", strings.Replace(description, "150 ml ou 250 ml", "150 ml ou 15 cl", 1), "Reusable bottle", "", false},
		{"selected query", description, "Reusable bottle", "?size=250", false},
		{"other product heading", description, "Another product", "", false},
		{"hidden description", strings.Replace(description, `<div `, `<div hidden `, 1), "Reusable bottle", "", false},
		{"recommendation", `<aside>` + description + `</aside>`, "Reusable bottle", "", false},
		{"recommendation article", `<article>` + description + `</article>`, "Reusable bottle", "", false},
		{"recommendation with matching heading", `<article><h1>Reusable bottle</h1>` + description + `</article>`, "Reusable bottle", "", false},
		{"product description class", strings.Replace(description, `itemprop="description"`, `class="product-description"`, 1), "Reusable bottle", "", true},
		{"non-product description", strings.Replace(description, ` itemprop="description"`, "", 1), "Reusable bottle", "", false},
		{"single volume", strings.Replace(description, "150 ml ou 250 ml", "150 ml", 1), "Reusable bottle", "", false},
		{"dimension rather than capacity", strings.Replace(description, "Volume :", "Dimensions :", 1), "Reusable bottle", "", false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product" + tc.query)
			document := `<title>Reusable bottle</title><meta property="product:price:amount" content="15.90"><meta property="product:price:currency" content="EUR"><main><h1>` + tc.heading + `</h1>` + tc.description + `</main>`
			// Act
			result := importer.Extract([]byte(document), base)
			// Assert
			if result.PriceAmbiguous != tc.ambiguous || (result.Price == nil) != tc.ambiguous {
				t.Fatalf("unexpected price or ambiguity: price=%v ambiguous=%v", result.Price, result.PriceAmbiguous)
			}
		})
	}
}

func TestPreview_WhenDescriptionListsUnselectedVolumes_DoesNotSpendFallback(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	page.get = func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		document := `<script type="application/ld+json">{"@type":"Product","name":"Reusable bottle","image":"/photo"}</script><main><h1>Reusable bottle</h1><div itemprop="description"><p>Volume : 150 ml ou 250 ml</p></div></main>`
		return importer.Extract([]byte(document), target), nil
	}
	calls := 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		calls++
		return importer.Metadata{}, nil
	}}
	// Act
	result, err := client.Preview(t.Context(), "https://merchant.test/product")
	// Assert
	if err != nil || calls != 0 || result.Name == nil || result.Image == nil || result.Price != nil {
		t.Fatalf("unselected volume spent fallback or produced a price: calls=%d, err=%v", calls, err)
	}
}
