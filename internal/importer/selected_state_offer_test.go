package importer_test

import (
	"html"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenComponentSelectsOfferInAggregateRange_RequiresBoundedDisplayedEvidence(t *testing.T) {
	const product = `{"@type":"Book","name":"Notebook","url":"/item","offers":{"@type":"AggregateOffer","url":"/item","lowPrice":"21.99","highPrice":"26.99","offerCount":2,"priceCurrency":"EUR","availability":"https://schema.org/InStock"}}`
	const state = `{"selectedSku":"selected","offers":[{"sku":"selected","price":999999},{"sku":"other","price":1}]}`
	const button = `<button data-component-sku-param="selected">Good condition <b>21,99 €</b></button>`
	for _, tc := range []struct{ name, product, state, button, before, after, query, want string }{
		{name: "explicit SKU binding", want: "21.99"},
		{name: "dot amount", button: strings.Replace(button, "21,99 €", "26.99 EUR", 1), want: "26.99"},
		{name: "plain data SKU", button: strings.Replace(button, "data-component-sku-param", "data-sku", 1), want: "21.99"},
		{name: "integer state prices not guessed", state: strings.Replace(state, "999999", "2199", 1), want: "21.99"},
		{name: "no selected SKU", state: `{"offers":[{"sku":"selected"},{"sku":"other"}]}`},
		{name: "SKU not in offers", state: strings.Replace(state, `"selectedSku":"selected"`, `"selectedSku":"unknown"`, 1)},
		{name: "duplicate selected SKU", state: strings.Replace(state, `"sku":"other"`, `"sku":"selected"`, 1)},
		{name: "different offer count", product: strings.Replace(product, `"offerCount":2`, `"offerCount":3`, 1)},
		{name: "missing range maximum", product: strings.Replace(product, `"highPrice":"26.99",`, "", 1)},
		{name: "reversed range", product: strings.Replace(product, `"highPrice":"26.99"`, `"highPrice":"20"`, 1)},
		{name: "wrong product URL", product: strings.ReplaceAll(product, `"/item"`, `"/different"`)},
		{name: "unavailable offer", product: strings.Replace(product, "InStock", "OutOfStock", 1)},
		{name: "foreign currency", product: strings.Replace(product, "EUR", "USD", 1)},
		{name: "below range", button: strings.Replace(button, "21,99", "19,99", 1)},
		{name: "above range", button: strings.Replace(button, "21,99", "29,99", 1)},
		{name: "zero visible price", button: strings.Replace(button, "21,99", "0", 1)},
		{name: "no currency", button: strings.Replace(button, " €", "", 1)},
		{name: "wrong currency", button: strings.Replace(button, "€", "USD", 1)},
		{name: "grouping not guessed", button: strings.Replace(button, "21,99", "1 021,99", 1)},
		{name: "currency splits decimals", button: strings.Replace(button, "21,99 €", "21€99", 1)},
		{name: "two visible prices", button: strings.Replace(button, "21,99 €", "21,99 € or 26,99 €", 1)},
		{name: "wrong bound SKU", button: strings.Replace(button, `sku-param="selected"`, `sku-param="other"`, 1)},
		{name: "disabled button", button: strings.Replace(button, "<button ", "<button disabled ", 1)},
		{name: "aria disabled", button: strings.Replace(button, "<button ", `<button aria-disabled="true" `, 1)},
		{name: "duplicate bound controls", button: button + button},
		{name: "unknown query not a default", query: "?variant=other"},
		{name: "duplicate heading", before: "<h1>Notebook</h1>"},
		{name: "recommendation article excluded", button: "<article>" + button + "</article>"},
		{name: "oversized button", button: strings.Replace(button, "Good condition", strings.Repeat("x", 2049), 1)},
		{name: "oversized state", state: `{"ignored":"` + strings.Repeat("x", 128*1024) + `"}`},
		{name: "malformed state", state: `{"selectedSku":false}`},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			p, s, b := product, state, button
			if tc.product != "" {
				p = tc.product
			}
			if tc.state != "" {
				s = tc.state
			}
			if tc.button != "" {
				b = tc.button
			}
			markup := `<script type="application/ld+json">` + p + `</script><article><h1>Notebook</h1>` + tc.before + `<div data-any-value="` + html.EscapeString(s) + `">` + b + `</div>` + tc.after + `</article>`
			base, _ := url.Parse("https://shop.test/item" + tc.query)
			// Act
			r := importer.Extract([]byte(markup), base)
			// Assert
			got := ""
			if r.Price != nil {
				got = r.Price.String()
			}
			if got != tc.want {
				t.Fatalf("got price %q want %q", got, tc.want)
			}
		})
	}
}
