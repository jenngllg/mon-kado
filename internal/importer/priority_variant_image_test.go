package importer_test

import (
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenSelectedColorHasUniquePriorityProductImage_PrefersVisibleImage(t *testing.T) {
	const structured = `<script type="application/ld+json">{"@type":"Product","name":"Pink rabbit","color":"Pink","image":"/group.jpg","isVariantOf":{"@type":"ProductGroup","name":"Rabbit"}}</script>`
	const picture = `<img class="responsive product" fetchpriority="high" width="400" height="400" src="/pink.jpg">`
	for _, tc := range []struct {
		name, schema, markup, expected string
	}{
		{"visible selected color", structured, picture, "https://merchant.test/pink.jpg"},
		{"no selected color", strings.Replace(structured, `"color":"Pink",`, "", 1), picture, "https://merchant.test/group.jpg"},
		{"no variant relationship", strings.Replace(structured, `"isVariantOf":{"@type":"ProductGroup","name":"Rabbit"}`, `"isVariantOf":{"@type":"Product","name":"Rabbit"}`, 1), picture, "https://merchant.test/group.jpg"},
		{"unrelated high priority image", structured, strings.Replace(picture, "responsive product", "banner", 1), "https://merchant.test/group.jpg"},
		{"small thumbnail", structured, strings.Replace(picture, `width="400"`, `width="64"`, 1), "https://merchant.test/group.jpg"},
		{"hidden image", structured, `<div hidden>` + picture + `</div>`, "https://merchant.test/group.jpg"},
		{"hidden class", structured, `<div class="hidden">` + picture + `</div>`, "https://merchant.test/group.jpg"},
		{"hidden style", structured, `<div style="display: none">` + picture + `</div>`, "https://merchant.test/group.jpg"},
		{"duplicate identical markup", structured, picture + picture, "https://merchant.test/pink.jpg"},
		{"multiple candidates", structured, picture + strings.Replace(picture, "/pink.jpg", "/other.jpg", 1), "https://merchant.test/group.jpg"},
		{"invalid address", structured, strings.Replace(picture, "/pink.jpg", "javascript:alert(1)", 1), "https://merchant.test/group.jpg"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product")
			// Act
			result := importer.Extract([]byte(tc.schema+tc.markup), base)
			// Assert
			if result.Image == nil || *result.Image != tc.expected {
				t.Fatalf("unexpected product image: %v", result.Image)
			}
		})
	}
}
