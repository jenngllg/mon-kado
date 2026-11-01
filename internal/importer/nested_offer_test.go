package importer_test

import (
	"fmt"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenQueryIdentifiesNestedOffer_PreservesExactSelection(t *testing.T) {
	const selected = `{"@type":"Offer","url":"/item?seller=chosen","price":"35.72","priceCurrency":"EUR"}`
	const other = `{"@type":"Offer","url":"/item?seller=other","price":"21.99","priceCurrency":"EUR"}`
	for _, tc := range []struct{ name, offers, query, want string }{
		{"one nested offer", `{"@type":"AggregateOffer","offers":[` + other + `,` + selected + `]}`, "?seller=chosen", "35.72"},
		{"deep aggregate", `{"@type":"AggregateOffer","offers":{"@type":"AggregateOffer","offers":[` + selected + `,` + other + `]}}`, "?seller=chosen", "35.72"},
		{"unknown selection", `{"@type":"AggregateOffer","offers":[` + other + `,` + selected + `]}`, "?seller=unknown", ""},
		{"no selection", `{"@type":"AggregateOffer","offers":[` + other + `,` + selected + `]}`, "", ""},
		{"different path", `{"@type":"AggregateOffer","offers":[` + other + `,{"@type":"Offer","url":"/recommendation?seller=chosen","price":"9","priceCurrency":"EUR"}]}`, "?seller=chosen", ""},
		{"different authority", `{"@type":"AggregateOffer","offers":[` + other + `,{"@type":"Offer","url":"https://other.test/item?seller=chosen","price":"9","priceCurrency":"EUR"}]}`, "?seller=chosen", ""},
		{"conflicting selected offers", `[{"@type":"Offer","url":"/item?seller=chosen","price":"40","priceCurrency":"EUR"},{"@type":"AggregateOffer","offers":[` + other + `,` + selected + `]}]`, "?seller=chosen", ""},
		{"agreeing selected offers", `[` + selected + `,{"@type":"AggregateOffer","offers":[` + other + `,` + selected + `]}]`, "?seller=chosen", "35.72"},
		{"selected price missing", `{"@type":"AggregateOffer","offers":[` + other + `,{"@type":"Offer","url":"/item?seller=chosen","priceCurrency":"EUR"}]}`, "?seller=chosen", ""},
		{"container identity not child identity", `{"@type":"AggregateOffer","url":"/item?seller=chosen","offers":[` + other + `,{"@type":"Offer","price":"40","priceCurrency":"EUR"}]}`, "?seller=chosen", ""},
		{"bounded depth", strings.Repeat(`{"@type":"AggregateOffer","offers":`, 66) + selected + strings.Repeat(`}`, 66), "?seller=chosen", ""},
		{"bounded candidates", `{"@type":"AggregateOffer","offers":[` + strings.Repeat(other+`,`, 256) + selected + `]}`, "?seller=chosen", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/item" + tc.query)
			markup := fmt.Sprintf(`<script type="application/ld+json">{"@type":"Product","name":"An item","url":"/item","offers":%s}</script>`, tc.offers)
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
