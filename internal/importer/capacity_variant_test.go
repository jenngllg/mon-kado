package importer_test

import (
	"fmt"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenCapacityVaries_RequiresAnIdentifiedVariant(t *testing.T) {
	const variants = `{"@type":"ProductGroup","name":"Hand cream","hasVariant":[{"@type":"Product","size":"30 ml","url":"/cream?capacity=30","offers":{"@type":"Offer","price":"9","priceCurrency":"EUR"}},{"@type":"Product","size":"150 ml","url":"/cream?capacity=150","offers":{"@type":"Offer","price":"27","priceCurrency":"EUR"}}]}`
	for _, tc := range []struct{ name, query, selection, want string }{
		{"placeholder leaves price empty", "", `<select><option selected>Choose capacity</option><option>30 ml</option><option>150 ml</option></select>`, ""},
		{"URL selects capacity", "?capacity=150", "", "27"},
		{"explicit control selects capacity", "", `<select><option>30 ml</option><option selected>150 ml</option></select>`, "27"},
		{"URL takes priority over default control", "?capacity=150", `<select><option selected>30 ml</option><option>150 ml</option></select>`, "27"},
		{"unknown capacity is not guessed", "?capacity=75", `<select><option selected>30 ml</option><option>150 ml</option></select>`, ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/cream" + tc.query)
			markup := fmt.Sprintf(`<script type="application/ld+json">%s</script>%s`, variants, tc.selection)
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
