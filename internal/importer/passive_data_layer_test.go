package importer_test

import (
	"encoding/json"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenSerializedCommerceLayerHasSelectedOffer_RequiresCorroboratedIdentityAndPrice(t *testing.T) {
	// Arrange
	const offer = `{"offerID":"selected","price":{"price":12.34,"priceWithTax":12.34,"shipping":0,"currency":"EUR"}}`
	const baseLayer = `{"product":[{"productInfo":{"productName":"Gift","productURL":"https://shop.test/product"},"attributes":{"availabilityType":"in-stock","offerID":"selected","currentOffer":` + offer + `,"offer":[` + offer + `]}}]}`
	for _, tc := range []struct {
		name, layer, heading, visible, extra, query, want string
	}{
		{"selected offer", baseLayer, "Gift", "12,34 €", "", "", "12.34"},
		{"different product", strings.Replace(baseLayer, "/product", "/other", 1), "Gift", "12,34 €", "", "", ""},
		{"different heading", baseLayer, "Other gift", "12,34 €", "", "", ""},
		{"no visible corroboration", baseLayer, "Gift", "13,34 €", "", "", ""},
		{"hidden price", baseLayer, "Gift", "", `<p hidden>12,34 €</p>`, "", ""},
		{"unresolved select", baseLayer, "Gift", "12,34 €", `<select><option>Small</option><option>Large</option></select>`, "", ""},
		{"unresolved radio group", baseLayer, "Gift", "12,34 €", `<div role="radiogroup"><button role="radio">Small</button><button role="radio">Large</button></div>`, "", ""},
		{"query selection", baseLayer, "Gift", "12,34 €", "", "?size=large", ""},
		{"wrong currency", strings.ReplaceAll(baseLayer, `"EUR"`, `"USD"`), "Gift", "12,34 €", "", "", ""},
		{"shipping included", strings.ReplaceAll(baseLayer, `"shipping":0`, `"shipping":2`), "Gift", "12,34 €", "", "", ""},
		{"unavailable", strings.Replace(baseLayer, "in-stock", "out-of-stock", 1), "Gift", "12,34 €", "", "", ""},
		{"offer not selected", strings.Replace(baseLayer, `"offerID":"selected","currentOffer"`, `"offerID":"other","currentOffer"`, 1), "Gift", "12,34 €", "", "", ""},
		{"duplicate offer", strings.Replace(baseLayer, `"offer":[`+offer+`]`, `"offer":[`+offer+`,`+offer+`]`, 1), "Gift", "12,34 €", "", "", ""},
		{"different selected offer price", strings.Replace(baseLayer, `"offer":[`+offer+`]`, `"offer":[`+strings.Replace(offer, `12.34`, `13.34`, 2)+`]`, 1), "Gift", "12,34 €", "", "", ""},
		{"structured conflict", baseLayer, "Gift", "12,34 €", `<script type="application/ld+json">{"@type":"Product","offers":[{"price":"10","priceCurrency":"EUR"},{"price":"20","priceCurrency":"EUR"}]}</script>`, "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			encoded, err := json.Marshal(tc.layer)
			if err != nil {
				t.Fatal(err)
			}
			markup := `<h1>` + tc.heading + `</h1><p>` + tc.visible + `</p>` + tc.extra + `<script>consume(` + string(encoded) + `)</script>`
			base, _ := url.Parse("https://shop.test/product" + tc.query)

			// Act
			result := importer.Extract([]byte(markup), base)

			// Assert
			got := ""
			if result.Price != nil {
				got = result.Price.String()
			}
			if got != tc.want {
				t.Fatalf("price = %q, want %q", got, tc.want)
			}
		})
	}
}
