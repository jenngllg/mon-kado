package importer_test

import (
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenProductHeadingOwnsMicrodata_SelectsOnlyUnambiguousPrimaryItem(t *testing.T) {
	const primary = `<div itemscope itemtype="https://schema.org/Product"><meta itemprop="name" content="Notebook"><h1>Notebook</h1><div itemprop="offers" itemscope itemtype="https://schema.org/Offer"><meta itemprop="price" content="10"><meta itemprop="priceCurrency" content="EUR"></div></div>`
	const recommendation = `<article itemscope itemtype="https://schema.org/Product"><meta itemprop="name" content="Lamp"><div itemprop="offers" itemscope itemtype="https://schema.org/Offer"><meta itemprop="price" content="20"><meta itemprop="priceCurrency" content="EUR"></div></article>`
	for _, tc := range []struct{ name, main, related, want string }{
		{"owned heading", primary, recommendation, "10"},
		{"heading elsewhere", strings.Replace(primary, "<h1>Notebook</h1>", "", 1), "<h1>Notebook</h1>" + recommendation, ""},
		{"heading does not match name", strings.Replace(primary, "<h1>Notebook", "<h1>Different", 1), recommendation, ""},
		{"multiple primary scopes", primary, strings.Replace(recommendation, "<meta itemprop=", "<h1>Lamp</h1><meta itemprop=", 1), ""},
		{"declared different URL", strings.Replace(primary, "<h1>", `<link itemprop="url" href="/different"><h1>`, 1), recommendation, ""},
		{"declared different item ID", strings.Replace(primary, "<div itemscope", `<div itemid="/different" itemscope`, 1), recommendation, ""},
		{"conflicting declared URLs", strings.Replace(primary, "<h1>", `<link itemprop="url" href="/first"><link itemprop="url" href="/second"><h1>`, 1), recommendation, ""},
		{"explicit page identity retains priority", primary, strings.Replace(recommendation, "<meta itemprop=", `<link itemprop="url" href="/item"><meta itemprop=`, 1), "20"},
		{"equivalent declaration retains conflicting price", primary, strings.ReplaceAll(recommendation, "Lamp", "Notebook"), ""},
		{"bounded equivalent declarations", primary, strings.Repeat(strings.ReplaceAll(strings.ReplaceAll(recommendation, "Lamp", "Notebook"), `content="20"`, `content="10"`), 255), "10"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/item")
			// Act
			result := importer.Extract([]byte(tc.main+tc.related), base)
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
