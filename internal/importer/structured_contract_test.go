package importer_test

import (
	"github.com/JennGllg/mon-kado/internal/importer"
	"net/url"
	"strings"
	"testing"
	"unicode/utf8"
)

func TestExtract_WhenStandardProductRepresentationsDiffer_RecoversOnlyConsistentSuggestions(t *testing.T) {
	for _, tc := range []struct{ name, markup, wantName, wantPrice, wantImage string }{
		{"duplicate product descriptions", `<script type="application/ld+json">[{"@type":"Product","name":"Gift","url":"/product","image":"/photo"},{"@type":"Product","name":"Gift","url":"/product","offers":{"price":"12.30","priceCurrency":"EUR"}}]</script>`, "Gift", "12.3", "https://shop.test/photo"},
		{"duplicate conflicting amounts", `<script type="application/ld+json">[{"@type":"Product","name":"Gift","sku":"same","offers":{"price":"12.3","priceCurrency":"EUR"}},{"@type":"Product","name":"Gift","sku":"same","offers":{"price":"13","priceCurrency":"EUR"}}]</script>`, "Gift", "", ""},
		{"same name different SKU", `<script type="application/ld+json">[{"@type":"Product","name":"Gift","sku":"A"},{"@type":"Product","name":"Gift","sku":"B"}]</script>`, "", "", ""},
		{"same name different product URLs", `<script type="application/ld+json">[{"@type":"Product","name":"Gift","url":"/a"},{"@type":"Product","name":"Gift","url":"/b"}]</script>`, "", "", ""},
		{"canonical selects product", `<link rel="canonical" href="/canonical"><script type="application/ld+json">[{"@type":"Product","name":"Recommendation","url":"/other"},{"@type":"Product","name":"Main","url":"/canonical","offers":{"price":"14","priceCurrency":"EUR"}}]</script>`, "Main", "14", ""},
		{"explicit original variant beats canonical", `<link rel="canonical" href="/product"><script type="application/ld+json">[{"@type":"Product","name":"Plain","url":"/product"},{"@type":"Product","name":"Large","url":"/product?variant=large","offers":{"price":"25","priceCurrency":"EUR"}}]</script>`, "Large", "25", ""},
		{"group equal variant prices", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Shoes","image":"/shoes","hasVariant":[{"@type":"Product","sku":"S","offers":{"price":"50","priceCurrency":"EUR"}},{"@type":"Product","sku":"M","offers":{"price":"50.00","priceCurrency":"EUR"}}]}</script>`, "Shoes", "50", "https://shop.test/shoes"},
		{"group varying prices", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Shoes","hasVariant":[{"@type":"Product","offers":{"price":"50","priceCurrency":"EUR"}},{"@type":"Product","offers":{"price":"60","priceCurrency":"EUR"}}]}</script>`, "Shoes", "", ""},
		{"group exact variant", `<script type="application/ld+json">{"@type":"https://schema.org/ProductGroup","name":"Shoes","hasVariant":[{"@type":"Product","url":"/product?variant=small","offers":{"price":"50","priceCurrency":"EUR"}},{"@type":"Product","url":"/product?variant=large","image":{"contentUrl":"/large"},"offers":{"price":"60","priceCurrency":"EUR"}}]}</script>`, "Shoes", "60", "https://shop.test/large"},
		{"main entity product", `<script type="application/ld+json">{"@type":"WebPage","mainEntity":{"@type":"Product","name":"Main","offers":{"price":"9","priceCurrency":"EUR"}}}</script>`, "Main", "9", ""},
		{"concrete offer before aggregate range", `<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":[{"@type":"Offer","price":"20","priceCurrency":"EUR"},{"@type":"AggregateOffer","lowPrice":"20","highPrice":"60","priceCurrency":"EUR"}]}</script>`, "Gift", "20", ""},
		{"single aggregate price", `<script type="application/ld+json">{"@type":"Product","offers":{"@type":"AggregateOffer","lowPrice":"20.00","highPrice":"20","priceCurrency":"EUR"}}</script>`, "", "20", ""},
		{"single counted aggregate", `<script type="application/ld+json">{"@type":"Product","offers":{"@type":"AggregateOffer","lowPrice":"20","offerCount":1,"priceCurrency":"EUR"}}</script>`, "", "20", ""},
		{"range is not exact price", `<script type="application/ld+json">{"@type":"Product","offers":{"@type":"AggregateOffer","lowPrice":"20","highPrice":"40","priceCurrency":"EUR"}}</script>`, "", "", ""},
		{"nested concrete offer", `<script type="application/ld+json">{"@type":"Product","offers":{"@type":"AggregateOffer","offers":{"price":"20","priceCurrency":"EUR"}}}</script>`, "", "20", ""},
		{"unit price specification", `<script type="application/ld+json">{"@type":"Product","offers":{"@type":"Offer","priceCurrency":"EUR","priceSpecification":{"@type":"UnitPriceSpecification","price":"19.90"}}}</script>`, "", "19.9", ""},
		{"strikethrough specification ignored", `<script type="application/ld+json">{"@type":"Product","offers":{"priceSpecification":[{"priceType":"https://schema.org/StrikethroughPrice","price":"99","priceCurrency":"EUR"},{"price":"19.90","priceCurrency":"EUR"}]}}</script>`, "", "19.9", ""},
		{"descriptive name before volume label", `<meta property="og:title" content="Brand Iris Perfume"><script type="application/ld+json">{"@type":"Product","name":"30ml - Refillable"}</script>`, "Brand Iris Perfume", "", ""},
		{"real product alongside protection library", `<title>Actual gift</title><meta property="og:type" content="product"><meta property="og:image" content="/photo"><script src="/cdn-cgi/challenge-platform/runtime"></script>`, "Actual gift", "", "https://shop.test/photo"},
		{"storefront canonical rejected", `<link rel="canonical" href="/"><title>Storefront</title><meta property="og:image" content="/logo">`, "", "", ""},
		{"foreign category canonical rejected", `<link rel="canonical" href="https://us.shop.test/collections/all"><title>Storefront</title>`, "", "", ""},
		{"invalid canonical ignored", `<link rel="canonical" href=":bad"><title>Gift</title>`, "Gift", "", ""},
		{"h1 fallback", `<h1>Product name</h1>`, "Product name", "", ""},
		{"shared entity stub", `<script type="application/ld+json">[{"@type":"Product","@id":"/product","name":"Gift","offers":{"price":"19","priceCurrency":"EUR"}},{"@type":"Product","@id":"/product"}]</script>`, "Gift", "19", ""},
		{"volume fallback to page title", `<title>Brand Iris Perfume</title><script type="application/ld+json">{"@type":"Product","name":"30ml - Refillable"}</script>`, "Brand Iris Perfume", "", ""},
		{"variant image follows selection", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Shirt","hasVariant":[{"@type":"Product","url":"/product?variant=small","image":"/small"},{"@type":"Product","url":"/product?variant=large","image":"/large"}]}</script>`, "Shirt", "", "https://shop.test/large"},
		{"unselected colors not guessed", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Shirt","hasVariant":[{"@type":"Product","image":"/red"},{"@type":"Product","image":"/blue"}]}</script>`, "Shirt", "", ""},
		{"selected variant overrides group image", `<script type="application/ld+json">{"@type":"ProductGroup","name":"Shirt","image":"/default","hasVariant":[{"@type":"Product","url":"/product?variant=small","image":"/small"},{"@type":"Product","url":"/product?variant=large","image":"/large"}]}</script>`, "Shirt", "", "https://shop.test/large"},
		{"entities normalized", `<script type="application/ld+json">{"@type":"Product","name":"Gift &amp; set","image":"/photo?x=1&amp;y=2"}</script>`, "Gift & set", "", "https://shop.test/photo?x=1&y=2"},
		{"twitter image fallback", `<title>Gift</title><meta name="twitter:image" content="/twitter">`, "Gift", "", "https://shop.test/twitter"},
		{"linked image fallback", `<title>Gift</title><link rel="image_src" href="/linked">`, "Gift", "", "https://shop.test/linked"},
		{"branding image is not a product fallback", `<title>Gift</title><meta property="og:image" content="/assets/logo/logo.png"><meta name="twitter:image" content="/gift.jpg">`, "Gift", "", "https://shop.test/gift.jpg"},
		{"visible matching image", `<h1>Blue ceramic vase</h1><img alt="Blue ceramic vase" data-src="/lazy.jpg">`, "Blue ceramic vase", "", "https://shop.test/lazy.jpg"},
		{"unrelated images excluded", `<h1>Blue ceramic vase</h1><img alt="Store logo" src="/logo">`, "Blue ceramic vase", "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://shop.test/product?variant=large")
			// Act
			r := importer.Extract([]byte(tc.markup), base)
			// Assert
			n, p, i := "", "", ""
			if r.Name != nil {
				n = *r.Name
			}
			if r.Price != nil {
				p = r.Price.String()
			}
			if r.Image != nil {
				i = *r.Image
			}
			if n != tc.wantName || p != tc.wantPrice || i != tc.wantImage {
				t.Fatalf("got name %q price %q image %q", n, p, i)
			}
		})
	}
}

func TestExtract_WhenLongUnicodeNameHasControls_PreservesLengthAndRejectsInvalidText(t *testing.T) {
	// Arrange
	base, _ := url.Parse("https://shop.test/product")
	// Act
	r := importer.Extract([]byte(`<meta property="og:title" content="`+strings.Repeat("é", 110)+`">`), base)
	bad := importer.Extract([]byte("<script type='application/ld+json'>{\"@type\":\"Product\",\"name\":\"good\\u0000bad\"}</script>"), base)
	// Assert
	if r.Name == nil || utf8.RuneCountInString(*r.Name) != 100 || !utf8.ValidString(*r.Name) || bad.Name != nil {
		t.Fatal("name validation or Unicode truncation failed")
	}
}

func TestExtract_WhenProductCollectionIsExcessive_DoesNotSelectATruncatedCandidateSet(t *testing.T) {
	// Arrange
	base, _ := url.Parse("https://shop.test/product")
	products := strings.Repeat(`{"@type":"Product","name":"Gift"},`, 256) + `{"@type":"Product","name":"Other"}`
	// Act
	r := importer.Extract([]byte(`<script type="application/ld+json">[`+products+`]</script>`), base)
	// Assert
	if r.Name != nil || r.Price != nil || r.Image != nil {
		t.Fatal("excessive candidate set was partially trusted")
	}
}
