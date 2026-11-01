package importer_test

import (
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenProductDisplaysStartingPrice_DoesNotTreatMinimumAsExact(t *testing.T) {
	const metadata = `<title>Gift box</title><meta property="og:price:amount" content="5.95"><meta property="og:price:currency" content="EUR">`
	const block = `<div role="status"><p>À partir de</p><span>5.95 €</span></div>`
	for _, tc := range []struct {
		name, markup, query string
		ambiguous           bool
	}{
		{"unconfigured starting price", `<main><h1>Gift box</h1>` + block + `</main>`, "", true},
		{"tracking query", `<main><h1>Gift box</h1>` + block + `</main>`, "?utm_source=link", true},
		{"unknown selection", `<main><h1>Gift box</h1>` + block + `</main>`, "?option=large", false},
		{"different displayed amount", `<main><h1>Gift box</h1>` + strings.Replace(block, "5.95", "6.95", 1) + `</main>`, "", false},
		{"different product heading", `<main><h1>Another product</h1>` + block + `</main>`, "", false},
		{"hidden starting label", `<main><h1>Gift box</h1>` + strings.Replace(block, `<p>`, `<p hidden>`, 1) + `</main>`, "", false},
		{"unrelated recommendation", `<main><h1>Gift box</h1><article><h2>Other</h2>` + block + `</article></main>`, "", false},
		{"marketplace link", `<main><h1>Gift box</h1><a href="/marketplace">À partir de 5.95 € chez un marchand</a></main>`, "", false},
		{"nested marketplace link", `<main><h1>Gift box</h1><a href="/marketplace"><span>À partir de 5.95 €</span></a></main>`, "", false},
		{"ordinary price", `<main><h1>Gift box</h1>` + strings.Replace(block, "À partir de", "Prix", 1) + `</main>`, "", false},
		{"compact split label and price", `<main><h1>Gift box</h1>` + strings.Replace(block, ` role="status"`, ``, 1) + `</main>`, "", true},
		{"starting price in another currency", `<main><h1>Gift box</h1><div><span>À partir de</span><span>29,90 $</span></div></main>`, "", true},
		{"starting price in another currency before amount", `<main><h1>Gift box</h1><div><span>À partir de</span><span>$ 29.90</span></div></main>`, "", true},
		{"foreign price in unrelated recommendation", `<main><h1>Gift box</h1><article><h2>Other</h2><p>À partir de 29,90 $</p></article></main>`, "", false},
		{"hidden foreign starting price", `<main><h1>Gift box</h1><div hidden><span>À partir de</span><span>29,90 $</span></div></main>`, "", false},
		{"hidden foreign amount", `<main><h1>Gift box</h1><div><span>À partir de</span><span hidden>29,90 $</span></div></main>`, "", false},
		{"struck foreign amount", `<main><h1>Gift box</h1><div><span>À partir de</span><del>29,90 $</del></div></main>`, "", false},
		{"several foreign prices", `<main><h1>Gift box</h1><div><span>À partir de</span><span>29,90 $ ou 49,90 $</span></div></main>`, "", false},
		{"two amounts in unmarked block", `<main><h1>Gift box</h1><div><p>À partir de</p><span>5.95 € ou 9.95 €</span></div></main>`, "", false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product" + tc.query)
			// Act
			result := importer.Extract([]byte(metadata+tc.markup), base)
			// Assert
			if tc.ambiguous {
				if result.Price != nil || !result.PriceAmbiguous {
					t.Fatalf("starting price was treated as exact: %v", result.Price)
				}
			} else if result.Price == nil || result.Price.String() != "5.95" || result.PriceAmbiguous {
				t.Fatalf("unrelated label changed the price: %v", result.Price)
			}
		})
	}
}
