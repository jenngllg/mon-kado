package importer_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenSchemaEntityHasOffers_PreservesCommerceIdentity(t *testing.T) {
	for _, tc := range []struct{ name, document, wantName, wantPrice, wantImage string }{
		{"book", `{"@type":"Book","name":"A book","isbn":"9780000000001","image":"/cover.webp","offers":{"@type":"Offer","url":"/item","price":14.50,"priceCurrency":"EUR","availability":"https://schema.org/OutOfStock"}}`, "A book", "14.5", "https://shop.test/cover.webp"},
		{"offered work without type list", `{"@type":"MusicRecording","name":"A recording","offers":{"@type":"Offer","price":"8.99","priceCurrency":"EUR"}}`, "A recording", "8.99", ""},
		{"qualified service", `{"@type":["Thing","https://schema.org/Service"],"name":"A service","offers":{"@type":"http://schema.org/Offer","price":"39","priceCurrency":"EUR"}}`, "A service", "39", ""},
		{"main entity", `{"@type":"WebPage","mainEntity":{"@type":"Course","name":"A course","offers":{"@type":"Offer","price":"45","priceCurrency":"EUR"}}}`, "A course", "45", ""},
		{"linked offer", `{"@graph":[{"@type":"Book","name":"Linked book","offers":{"@id":"#sale"}},{"@type":"Offer","@id":"#sale","price":"12.90","priceCurrency":"EUR"}]}`, "Linked book", "12.9", ""},
		{"ambiguous event prices", `{"@type":"Event","name":"Concert","offers":{"@type":"AggregateOffer","lowPrice":"20","highPrice":"40","priceCurrency":"EUR"}}`, "Concert", "", ""},
		{"foreign currency", `{"@type":"Book","name":"A book","offers":{"@type":"Offer","price":"20","priceCurrency":"USD"}}`, "A book", "", ""},
		{"zero is unavailable", `{"@type":"Book","name":"A book","offers":{"@type":"Offer","price":"0","priceCurrency":"EUR"}}`, "A book", "", ""},
		{"different editions", `[{"@type":"Book","name":"Same title","isbn":"9780000000001","offers":{"@type":"Offer","url":"/different","price":"8","priceCurrency":"EUR"}},{"@type":"Book","name":"Same title","isbn":"9780000000002","offers":{"@type":"Offer","url":"/item","price":"16","priceCurrency":"EUR"}}]`, "Same title", "16", ""},
		{"catalogue not merchandise", `{"@type":"OfferCatalog","name":"Catalogue","offers":{"@type":"Offer","price":"1","priceCurrency":"EUR"}}`, "Page", "", ""},
		{"organization not merchandise", `{"@type":"Organization","name":"Company","offers":{"@type":"Offer","price":"1","priceCurrency":"EUR"}}`, "Page", "", ""},
		{"untyped value not merchandise", `{"name":"Unknown","offers":{"@type":"Offer","price":"1","priceCurrency":"EUR"}}`, "Page", "", ""},
		{"untyped offer not sufficient", `{"@type":"Book","name":"Unverified","offers":{"price":"1","priceCurrency":"EUR"}}`, "Page", "", ""},
		{"demand is not a sale", `{"@type":"Book","name":"Wanted book","offers":{"@type":"Demand","price":"1","priceCurrency":"EUR"}}`, "Page", "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/item")
			markup := `<title>Page</title><script type="application/ld+json">` + tc.document + `</script>`
			// Act
			result := importer.Extract([]byte(markup), base)
			// Assert
			name, amount, image := "", "", ""
			if result.Name != nil {
				name = *result.Name
			}
			if result.Price != nil {
				amount = result.Price.String()
			}
			if result.Image != nil {
				image = *result.Image
			}
			if name != tc.wantName || amount != tc.wantPrice || image != tc.wantImage {
				t.Fatalf("got name %q price %q image %q", name, amount, image)
			}
		})
	}
}
