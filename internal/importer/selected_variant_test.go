package importer_test

import (
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenMarkupSelectsStructuredVariant_UsesOnlyUniqueSelection(t *testing.T) {
	const variants = `{"@type":"ProductGroup","name":"A print","hasVariant":[{"@type":"Product","size":"30x40 cm","color":"Green","image":"/small.jpg","offers":{"price":"31.95","priceCurrency":"EUR"}},{"@type":"Product","size":"50x70 cm","color":"Red","image":"/large.jpg","offers":{"price":"41.95","priceCurrency":"EUR"}}]}`
	for _, tc := range []struct{ name, markup, query, price, image string }{
		{"aria selected option", `<ul role="listbox"><li role="option" aria-selected="true"><span>30x40 cm</span><span>31,95 €</span></li><li role="option" aria-selected="false">50x70 cm</li></ul>`, "", "31.95", "https://shop.test/small.jpg"},
		{"native selected option", `<select><option>30x40 cm</option><option selected>50x70 cm</option></select>`, "", "41.95", "https://shop.test/large.jpg"},
		{"radio label", `<input id="choice" type="radio" checked><label for="choice">50x70 cm</label>`, "", "41.95", "https://shop.test/large.jpg"},
		{"wrapping radio label", `<label>50x70 cm<input type="radio" checked></label>`, "", "41.95", "https://shop.test/large.jpg"},
		{"radio list item", `<li><input type="radio" checked><span>30x40 cm</span><span>Available</span></li>`, "", "31.95", "https://shop.test/small.jpg"},
		{"explicit value", `<select><option value="30x40 cm" selected>Small</option></select>`, "", "31.95", "https://shop.test/small.jpg"},
		{"case and whitespace", `<select><option selected>  30X40   CM </option></select>`, "", "31.95", "https://shop.test/small.jpg"},
		{"selected color", `<div role="option" aria-selected="true" aria-label="Red"></div>`, "", "41.95", "https://shop.test/large.jpg"},
		{"conflicting dimensions", `<select><option selected>30x40 cm</option></select><div role="option" aria-selected="true">Red</div>`, "", "", ""},
		{"multiple selected sizes", `<select multiple><option selected>30x40 cm</option><option selected>50x70 cm</option></select>`, "", "", ""},
		{"unselected option", `<select><option>30x40 cm</option><option>50x70 cm</option></select>`, "", "", ""},
		{"unrelated control", `<select><option selected>Popularity</option></select>`, "", "", ""},
		{"disabled radio", `<label>30x40 cm<input type="radio" checked disabled></label>`, "", "", ""},
		{"checkbox not variant", `<label>30x40 cm<input type="checkbox" checked></label>`, "", "", ""},
		{"query selection not guessed", `<option selected>30x40 cm</option>`, "?variant=unknown", "", ""},
		{"price text not selection", `<div>30x40 cm : 31.95 EUR</div>`, "", "", ""},
		{"scripts are not labels", `<div role="option" aria-selected="true"><script>30x40 cm</script></div>`, "", "", ""},
		{"arbitrary ancestors not labels", `<form><span>30x40 cm</span><div><input type="radio" checked></div></form>`, "", "", ""},
		{"selection work bounded", `<div role="option" aria-selected="true">` + strings.Repeat(`<i>x</i>`, 128) + `<span>30x40 cm</span></div>`, "", "", ""},
		{"selection count bounded", strings.Repeat(`<option selected>30x40 cm</option>`, 33), "", "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/item" + tc.query)
			markup := `<script type="application/ld+json">` + variants + `</script>` + tc.markup
			// Act
			r := importer.Extract([]byte(markup), base)
			// Assert
			price, image := "", ""
			if r.Price != nil {
				price = r.Price.String()
			}
			if r.Image != nil {
				image = *r.Image
			}
			if price != tc.price || image != tc.image {
				t.Fatalf("got price %q image %q", price, image)
			}
		})
	}
}
