package importer_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenTitleIsOnlySiteBrand_DoesNotMaskProductName(t *testing.T) {
	for _, tc := range []struct{ name, host, markup, want string }{
		{"unloaded shell", "gift-shop.fr", `<title>Gift Shop</title><meta property="og:title" content="Gift Shop">`, ""},
		{"registrable domain as title", "www.gift-shop.fr", `<title>gift-shop.fr</title>`, ""},
		{"full host as title", "shop.gift-shop.fr", `<title>shop.gift-shop.fr</title>`, ""},
		{"full domain with product heading", "www.gift-shop.fr", `<title>gift-shop.fr</title><h1>Ceramic lamp</h1>`, "Ceramic lamp"},
		{"site metadata", "store.test", `<meta property="og:site_name" content="Example Brand"><meta property="og:title" content="Example Brand">`, ""},
		{"real heading behind brand metadata", "gift-shop.fr", `<title>Gift Shop</title><meta property="og:title" content="Gift Shop"><h1>A personalised photo album</h1>`, "A personalised photo album"},
		{"country suffix", "shop.example.co.uk", `<title>Example</title><h1>Ceramic workshop</h1>`, "Ceramic workshop"},
		{"ordinary title retained", "gift-shop.fr", `<title>A personalised photo album</title>`, "A personalised photo album"},
		{"brand plus product retained", "gift-shop.fr", `<title>Gift Shop - A personalised photo album</title>`, "Gift Shop - A personalised photo album"},
		{"structured name retains authority", "gift-shop.fr", `<title>Gift Shop</title><script type="application/ld+json">{"@type":"Product","name":"Gift Shop","offers":{"price":"10","priceCurrency":"EUR"}}</script>`, "Gift Shop"},
		{"unnamed structured item uses actual heading", "gift-shop.fr", `<title>Gift Shop</title><script type="application/ld+json">{"@type":"Product","sku":"one","offers":{"price":"10","priceCurrency":"EUR"}}</script><h1>A photo album</h1>`, "A photo album"},
		{"invalid registrable domain does not guess brand", "localhost", `<title>Local workshop</title>`, "Local workshop"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://" + tc.host + "/item")
			// Act
			result := importer.Extract([]byte(tc.markup), base)
			// Assert
			actual := ""
			if result.Name != nil {
				actual = *result.Name
			}
			if actual != tc.want {
				t.Fatalf("got name %q, want %q", actual, tc.want)
			}
		})
	}
}
