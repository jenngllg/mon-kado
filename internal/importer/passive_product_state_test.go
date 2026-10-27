package importer_test

import (
	"html"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenPassiveStateIdentifiesPrimaryProduct_RequiresExactPriceEvidence(t *testing.T) {
	const product = `{"@type":"Product","name":"Notebook | Shop","sku":"parent-sku","url":"/item"}`
	const state = `{"code":"parent-sku","name":"Notebook","children":{"one":{"code":"child-sku","name":"Notebook","price":"1,80 €","rawPrice":180,"originalPrice":"3,50 €","noVatPrice":"1,50 €"}}}`
	for _, tc := range []struct{ name, product, state, headings, query, want string }{
		{name: "explicit formatted euro amount", want: "1.8"},
		{name: "SKU field", state: strings.Replace(state, `"code":"parent-sku"`, `"sku":"parent-sku"`, 1), want: "1.8"},
		{name: "same price across variants", state: strings.Replace(state, `"one":`, `"two":{"code":"second-sku","name":"Notebook","price":"1.80 EUR"},"one":`, 1), want: "1.8"},
		{name: "conflicting variant prices", state: strings.Replace(state, `"one":`, `"two":{"code":"second-sku","name":"Notebook","price":"4.80 EUR"},"one":`, 1)},
		{name: "missing variant price", state: strings.Replace(state, `"one":`, `"two":{"code":"second-sku","name":"Notebook"},"one":`, 1)},
		{name: "different named product not borrowed", state: strings.Replace(state, `"one":`, `"two":{"code":"second-sku","name":"Delivery","price":"4.80 EUR"},"one":`, 1)},
		{name: "unknown integer units", state: strings.Replace(state, `"price":"1,80 €"`, `"price":180`, 1)},
		{name: "raw and old prices ignored", state: strings.Replace(state, `"price":"1,80 €",`, "", 1)},
		{name: "zero price rejected", state: strings.Replace(state, "1,80 €", "0 €", 1)},
		{name: "foreign price rejected", state: strings.Replace(state, "1,80 €", "1,80 USD", 1)},
		{name: "mixed punctuation not guessed", state: strings.Replace(state, "1,80 €", "1.000,80 €", 1)},
		{name: "range not guessed", state: strings.Replace(state, "1,80 €", "From 1,80 €", 1)},
		{name: "wrong root identifier", state: strings.Replace(state, "parent-sku", "other-sku", 1)},
		{name: "missing root identifier", state: strings.Replace(state, `"code":"parent-sku",`, "", 1)},
		{name: "missing structured identifier", product: strings.Replace(product, `"sku":"parent-sku",`, "", 1)},
		{name: "different document", product: strings.Replace(product, `"/item"`, `"/different"`, 1)},
		{name: "unknown query is not default selection", query: "?variant=unknown"},
		{name: "explicit color query", state: strings.Replace(state, `"code":"child-sku"`, `"code":"child-sku","color":"green"`, 1), query: "?color=green", want: "1.8"},
		{name: "namespaced color query", state: strings.Replace(state, `"code":"child-sku"`, `"code":"child-sku","color":"green"`, 1), query: "?variant%5Bcolor%5D=green", want: "1.8"},
		{name: "query does not match variant", state: strings.Replace(state, `"code":"child-sku"`, `"code":"child-sku","color":"red"`, 1), query: "?variant%5Bcolor%5D=green"},
		{name: "query selects one conflicting sibling", state: strings.Replace(strings.Replace(state, `"code":"child-sku"`, `"code":"child-sku","color":"green"`, 1), `"one":`, `"two":{"code":"second-sku","name":"Notebook","color":"red","price":"4.80 EUR"},"one":`, 1), query: "?color=green", want: "1.8"},
		{name: "duplicate query values", query: "?color=green&color=red"},
		{name: "conflicting namespaced query", query: "?color=green&variant%5Bcolor%5D=red"},
		{name: "empty selected value", query: "?color="},
		{name: "malformed percent encoding", query: "?color=%"},
		{name: "oversized selected value", query: "?color=" + strings.Repeat("a", 129)},
		{name: "different heading", headings: "<h1>Recommendation</h1>"},
		{name: "duplicate heading", headings: "<h1>Notebook</h1><h1>Notebook</h1>"},
		{name: "no heading in component", headings: "<h2>Notebook</h2>"},
		{name: "structured ambiguity retained", product: strings.TrimSuffix(product, "}") + `,"offers":{"@type":"AggregateOffer","lowPrice":"1.8","highPrice":"9","priceCurrency":"EUR"}}`},
		{name: "unpriced structured offer retained", product: strings.TrimSuffix(product, "}") + `,"offers":{"@type":"Offer","url":"/item"}}`},
		{name: "invalid JSON", state: `{"code":"parent-sku"`},
		{name: "bounded attribute", state: strings.Replace(state, `"children":`, `"padding":"`+strings.Repeat("x", 128*1024)+`","children":`, 1)},
		{name: "bounded depth", state: strings.Replace(state, `"children":`, `"children":`+strings.Repeat(`{"nested":`, 18), 1) + strings.Repeat(`}`, 18)},
		{name: "bounded nodes", state: strings.Replace(state, `"one":`, `"padding":[`+strings.Repeat(`{},`, 256)+`{}],"one":`, 1)},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			p, s, heading := product, state, "<h1>Notebook</h1>"
			if tc.product != "" {
				p = tc.product
			}
			if tc.state != "" {
				s = tc.state
			}
			if tc.headings != "" {
				heading = tc.headings
			}
			markup := `<script type="application/ld+json">` + p + `</script><div data-universal-state="` + html.EscapeString(s) + `">` + heading + `</div>`
			base, _ := url.Parse("https://shop.test/item" + tc.query)
			// Act
			result := importer.Extract([]byte(markup), base)
			// Assert
			actual := ""
			if result.Price != nil {
				actual = result.Price.String()
			}
			if actual != tc.want {
				t.Fatalf("got price %q, want %q", actual, tc.want)
			}
		})
	}
}
