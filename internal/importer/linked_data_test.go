package importer_test

import (
	"fmt"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenStructuredEntitiesAreLinked_ResolvesLocalReferencesWithoutGuessing(t *testing.T) {
	for _, tc := range []struct{ name, markup, wantName, wantPrice, wantImage string }{
		{"cross script references", `<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"@id":"#offer"},"image":{"@id":"#photo"}}</script><script type="application/ld+json">{"@graph":[{"@id":"#offer","@type":"Offer","price":"12.30","priceCurrency":"EUR"},{"@id":"#photo","@type":"ImageObject","contentUrl":"/photo"}]}</script>`, "Gift", "12.3", "https://shop.test/photo"},
		{"shared price specification", `<script type="application/ld+json">[{"@type":"Product","offers":{"@id":"#offer"}},{"@id":"#offer","priceSpecification":{"@id":"#spec"}},{"@id":"#spec","price":"19.9","priceCurrency":"EUR"}]</script>`, "", "19.9", ""},
		{"unresolved external reference", `<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"@id":"https://other.test/offers"}}</script>`, "Gift", "", ""},
		{"conflicting offer definitions", `<script type="application/ld+json">[{"@type":"Product","name":"Gift","offers":{"@id":"#offer"}},{"@id":"#offer","price":"19","priceCurrency":"EUR"},{"@id":"#offer","price":"29","priceCurrency":"EUR"}]</script>`, "Gift", "", ""},
		{"equivalent offer definitions", `<script type="application/ld+json">[{"@type":"Product","offers":{"@id":"#offer"}},{"@id":"#offer","price":"19","priceCurrency":"EUR"},{"@id":"#offer","price":"19","priceCurrency":"EUR"}]</script>`, "", "19", ""},
		{"compatible typed offer stub", `<script type="application/ld+json">[{"@type":"Product","offers":{"@id":"#offer","@type":"Offer"}},{"@id":"#offer","@type":"Offer","price":"19","priceCurrency":"EUR"}]</script>`, "", "19", ""},
		{"cyclic offer", `<script type="application/ld+json">[{"@type":"Product","name":"Gift","offers":{"@id":"#offer"}},{"@id":"#offer","offers":{"@id":"#offer"}}]</script>`, "Gift", "", ""},
		{"unpriced colour with unanimous sizes", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Shoes","hasVariant":[{"url":"/product","image":"/selected"},{"url":"/different","offers":{"price":"200","priceCurrency":"EUR"}},{"url":"/product?size=S","offers":{"price":"110","priceCurrency":"EUR"}},{"url":"/product?size=M","offers":{"price":"110.00","priceCurrency":"EUR"}}]}</script>`, "Shoes", "110", "https://shop.test/selected"},
		{"unpriced colour with different size prices", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Shoes","hasVariant":[{"url":"/product"},{"url":"/product?size=S","offers":{"price":"110","priceCurrency":"EUR"}},{"url":"/product?size=M","offers":{"price":"120","priceCurrency":"EUR"}}]}</script>`, "Shoes", "", ""},
		{"microdata detached properties", `<article itemscope itemtype="https://schema.org/Product" itemref="price"><span itemprop="name">Gift</span></article><div id="price"><div itemprop="offers" itemscope itemtype="https://schema.org/Offer"><meta itemprop="price" content="24.95"><meta itemprop="priceCurrency" content="EUR"></div></div>`, "Gift", "24.95", ""},
		{"duplicate detached ID", `<article itemscope itemtype="https://schema.org/Product" itemref="name"></article><span id="name" itemprop="name">One</span><span id="name" itemprop="name">Two</span>`, "", "", ""},
		{"other scope owns detached property", `<article itemscope itemtype="https://schema.org/Product" itemref="name"></article><div itemscope><span id="name" itemprop="name">Other</span></div>`, "", "", ""},
		{"microdata nested price and image", `<article itemscope itemtype="https://schema.org/Product"><b itemprop="name">Gift</b><div itemprop="image" itemscope itemtype="https://schema.org/ImageObject"><link itemprop="contentUrl" href="/photo"></div><div itemprop="offers" itemscope itemtype="https://schema.org/Offer"><div itemprop="priceSpecification" itemscope itemtype="https://schema.org/UnitPriceSpecification"><meta itemprop="price" content="19.95"><meta itemprop="priceCurrency" content="EUR"></div></div></article>`, "Gift", "19.95", "https://shop.test/photo"},
		{"RDFa vocabulary", `<article vocab="https://schema.org/" typeof="Product"><span property="name">Gift</span><img property="image" src="/photo"><div property="offers" typeof="Offer"><meta property="price" content="25"><meta property="priceCurrency" content="EUR"></div></article>`, "Gift", "25", "https://shop.test/photo"},
		{"RDFa qualified prefix", `<article prefix="s: https://schema.org/" typeof="s:Product"><span property="s:name">Gift</span><div property="s:offers" typeof="s:Offer"><meta property="s:price" content="25"><meta property="s:priceCurrency" content="EUR"></div></article>`, "Gift", "25", ""},
		{"RDFa unknown vocabulary", `<article vocab="https://unrelated.test/" typeof="Product"><span property="name">Other</span></article>`, "", "", ""},
		{"RDFa absolute terms", `<article typeof="https://schema.org/Product"><span property="https://schema.org/name">Gift</span></article>`, "Gift", "", ""},
		{"standalone offer linked to page", `<title>Gift</title><div itemscope itemtype="https://schema.org/Offer"><link itemprop="url" href="/product"><meta itemprop="price" content="15"><meta itemprop="priceCurrency" content="EUR"></div>`, "Gift", "15", ""},
		{"unselected standalone offer", `<title>Gift</title><div itemprop="offers" itemscope itemtype="https://schema.org/Offer"><meta itemprop="price" content="15"><meta itemprop="priceCurrency" content="EUR"></div>`, "Gift", "", ""},
		{"recommendation offer", `<title>Gift</title><div itemscope itemtype="https://schema.org/Offer"><link itemprop="url" href="/recommendation"><meta itemprop="price" content="15"><meta itemprop="priceCurrency" content="EUR"></div>`, "Gift", "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/product")
			// Act
			result := importer.Extract([]byte(tc.markup), base)
			// Assert
			name, price, image := "", "", ""
			if result.Name != nil {
				name = *result.Name
			}
			if result.Price != nil {
				price = result.Price.String()
			}
			if result.Image != nil {
				image = *result.Image
			}
			if name != tc.wantName || price != tc.wantPrice || image != tc.wantImage {
				t.Fatalf("got name %q price %q image %q", name, price, image)
			}
		})
	}
}

func TestExtract_WhenLinkedDataExceedsDepthOrExpansionBudget_RejectsInsteadOfTruncating(t *testing.T) {
	for _, name := range []string{"depth", "expansion"} {
		t.Run(name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/product")
			data := strings.Repeat(`{"mainEntity":`, 66) + `{"@type":"Product","name":"Gift"}` + strings.Repeat(`}`, 66)
			if name == "expansion" {
				var nodes []string
				for i := 0; i < 20; i++ {
					nodes = append(nodes, fmt.Sprintf(`{"@id":"#%d","offers":[{"@id":"#%d"},{"@id":"#%d"}]}`, i, i+1, i+1))
				}
				data = `[{"@type":"Product","name":"Gift","offers":{"@id":"#0"}},` + strings.Join(nodes, ",") + `]`
			}
			// Act
			result := importer.Extract([]byte(`<title>Unsafe</title><script type="application/ld+json">`+data+`</script>`), base)
			// Assert
			if result.Name != nil || result.Price != nil || result.Image != nil {
				t.Fatal("oversized graph partially trusted")
			}
		})
	}
}
