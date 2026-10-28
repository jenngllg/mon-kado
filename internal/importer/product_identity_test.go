package importer_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenDuplicateProductsShareValidatedGTIN_ReconcilesIndependentSKUs(t *testing.T) {
	for _, test := range []struct {
		name, leftID, rightID, rightOffers string
		wantName, wantPrice                string
	}{
		{"product and numeric offer GTIN14", `"gtin14":"17841945768163",`, ``, `{"gtin14":17841945768163,"price":"69.95","priceCurrency":"EUR"}`, "Book nook", "69.95"},
		{"GTIN13 and zero padded GTIN14", `"gtin13":"6291041500213",`, `"gtin14":"06291041500213",`, `{"price":"69.95","priceCurrency":"EUR"}`, "Book nook", "69.95"},
		{"same GTIN8", `"gtin8":"96385074",`, `"gtin8":"96385074",`, `{"price":"69.95","priceCurrency":"EUR"}`, "Book nook", "69.95"},
		{"same GTIN12", `"gtin12":"012345600012",`, `"gtin12":"012345600012",`, `{"price":"69.95","priceCurrency":"EUR"}`, "Book nook", "69.95"},
		{"generic GTIN", `"gtin":"6291041500213",`, `"gtin13":"6291041500213",`, `{"price":"69.95","priceCurrency":"EUR"}`, "Book nook", "69.95"},
		{"equivalent declared GTIN fields", `"gtin":"6291041500213","gtin14":"06291041500213",`, `"gtin13":"6291041500213",`, `{"price":"69.95","priceCurrency":"EUR"}`, "Book nook", "69.95"},
		{"whitespace around identifier", `"gtin14":" 17841945768163 ",`, `"gtin14":"17841945768163",`, `{"price":"69.95","priceCurrency":"EUR"}`, "Book nook", "69.95"},
		{"GTIN conflict", `"gtin14":"17841945768163",`, `"gtin13":"6291041500213",`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"conflicting book editions", `"gtin14":"17841945768163","isbn":"9782266337199",`, `"gtin14":"17841945768163","isbn":"9782070368228",`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"invalid checksum", `"gtin14":"17841945768162",`, `"gtin14":"17841945768162",`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"invalid declared length", `"gtin14":"6291041500213",`, `"gtin14":"6291041500213",`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"non decimal identifier", `"gtin14":"1784194576816x",`, `"gtin14":"1784194576816x",`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"numeric exponent is not an identifier", `"gtin14":1.7841945768163e13,`, `"gtin14":1.7841945768163e13,`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"all zero placeholder", `"gtin14":"00000000000000",`, `"gtin14":"00000000000000",`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"boolean is not an identifier", `"gtin14":true,`, `"gtin14":true,`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"different offer variants", `"gtin14":"17841945768163",`, ``, `[{"gtin14":"17841945768163","price":"69.95","priceCurrency":"EUR"},{"gtin13":"6291041500213","price":"79.95","priceCurrency":"EUR"}]`, "", ""},
		{"unidentified sibling offer", `"gtin14":"17841945768163",`, ``, `[{"gtin14":"17841945768163","price":"69.95","priceCurrency":"EUR"},{"price":"79.95","priceCurrency":"EUR"}]`, "", ""},
		{"matching offer IDs retain price conflict", `"gtin14":"17841945768163",`, ``, `[{"gtin14":"17841945768163","price":"69.95","priceCurrency":"EUR"},{"gtin14":"17841945768163","price":"79.95","priceCurrency":"EUR"}]`, "Book nook", ""},
		{"conflicting GTIN properties", `"gtin14":"17841945768163","gtin13":"6291041500213",`, `"gtin14":"17841945768163",`, `{"price":"69.95","priceCurrency":"EUR"}`, "", ""},
		{"root and offer identity conflict", `"gtin14":"17841945768163",`, `"gtin14":"17841945768163",`, `{"gtin13":"6291041500213","price":"69.95","priceCurrency":"EUR"}`, "", ""},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/product")
			markup := `<script type="application/ld+json">[{"@type":"Product","name":"Book nook","url":"/product","sku":"feed-id",` + test.leftID + `"image":"/photo"},{"@type":"Product","name":"Book nook","url":"/product","sku":"theme-id",` + test.rightID + `"offers":` + test.rightOffers + `}]</script>`

			// Act
			result := importer.Extract([]byte(markup), base)

			// Assert
			name, price := "", ""
			if result.Name != nil {
				name = *result.Name
			}
			if result.Price != nil {
				price = result.Price.String()
			}
			if name != test.wantName || price != test.wantPrice {
				t.Fatalf("got name %q price %q", name, price)
			}
			if test.wantName != "" && (result.Image == nil || *result.Image != "https://shop.test/photo") {
				t.Fatal("duplicate product image was lost")
			}
		})
	}
}
