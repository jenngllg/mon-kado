package importer

import (
	"net/url"
	"testing"
)

func TestExtract_WhenUnstructuredProductShowsPrice_RequiresUnambiguousProductContext(t *testing.T) {
	for _, tc := range []struct {
		name, body, want string
	}{
		{"single price", `<h1>Green ceramic mug</h1><span class="product-price">12,50 €</span>`, "12.5"},
		{"split price", `<h1>Green ceramic mug</h1><span class="price"><span>12</span>,<span>50</span>€</span>`, "12.5"},
		{"product inside card", `<div class="product-card"><h1>Green ceramic mug</h1><span class="price">12 €</span></div>`, "12"},
		{"different amounts", `<h1>Green ceramic mug</h1><span class="price">12 €</span><span class="sale-price">19 €</span>`, ""},
		{"starting price parent", `<h1>Green ceramic mug</h1><span>À partir de <span class="price">12 €</span></span>`, ""},
		{"starting price des", `<h1>Green ceramic mug</h1><span>Dès <span class="price">12 €</span></span>`, ""},
		{"variant choices", `<h1>Green ceramic mug</h1><span class="price">12 €</span><select name="size"><option>S</option><option>L</option></select>`, ""},
		{"choice label", `<h1>Green ceramic mug</h1><span class="price">12 €</span><label for="option">Taille</label><select id="option"><option>S</option><option>L</option></select>`, ""},
		{"accessible group", `<h1>Green ceramic mug</h1><span class="price">12 €</span><span id="choice">Choisir la couleur</span><div role="radiogroup" aria-labelledby="choice"></div>`, ""},
		{"fieldset legend", `<h1>Green ceramic mug</h1><span class="price">12 €</span><fieldset><legend>Contenance</legend><input type="radio" name="option"></fieldset>`, ""},
		{"button choice", `<h1>Green ceramic mug</h1><span class="price">12 €</span><button>Choisir la taille</button>`, ""},
		{"hidden price", `<h1>Green ceramic mug</h1><span class="price is-hidden">12 €</span>`, ""},
		{"struck price", `<h1>Green ceramic mug</h1><del><span class="price">12 €</span></del>`, ""},
		{"recommendation link", `<h1>Green ceramic mug</h1><a href="/another"><span class="price">12 €</span></a>`, ""},
		{"recommendation article", `<h1>Green ceramic mug</h1><article><span class="price">12 €</span></article>`, ""},
		{"recommendation card", `<h1>Green ceramic mug</h1><div class="product-card"><h2>Red ceramic bowl</h2><span class="price">9 €</span></div>`, ""},
		{"previous price", `<h1>Green ceramic mug</h1><span class="old-price">12 €</span>`, ""},
		{"unrelated heading", `<h1>Red ceramic bowl</h1><span class="price">12 €</span>`, ""},
		{"multiple product headings", `<h1>Green ceramic mug</h1><h1>Green ceramic mug</h1><span class="price">12 €</span>`, ""},
		{"unsupported currency", `<h1>Green ceramic mug</h1><span class="price">12 USD</span>`, ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product")
			document := `<html><head><meta property="og:title" content="Green ceramic mug"></head><body><main><div>` + tc.body + `</div></main></body></html>`
			// Act
			got := Extract([]byte(document), base)
			// Assert
			if tc.want == "" {
				if got.Price != nil {
					t.Fatalf("uncertain price was selected: %v", *got.Price)
				}
			} else if got.Price == nil || got.Price.String() != tc.want {
				t.Fatalf("expected %s, got %v", tc.want, got.Price)
			}
		})
	}
}
