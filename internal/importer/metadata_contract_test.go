package importer_test

import (
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenConsolidatedMerchantScenariosExist_PreservesExactProductSuggestions(t *testing.T) {
	for _, test := range []struct {
		name, host, markup, wantName, wantPrice, wantImage string
		unsupported                                        bool
	}{
		{"exact page among recommendations", "merchant.test", `<script type="application/ld+json">[{"@type":"Product","name":"Other","url":"/other"},{"@type":"https://schema.org/Product","name":"Selected","url":"/product?variant=large","offers":[{"price":"29.90","priceCurrency":"EUR","url":"/other"},{"price":"97.30","priceCurrency":"EUR","url":"/product?variant=large"}]}]</script>`, "Selected", "97.3", "", false},
		{"ambiguous products", "merchant.test", `<title>Do not guess</title><script type="application/ld+json">[{"@type":"Product","name":"First"},{"@type":"Product","name":"Second"}]</script>`, "", "", "", false},
		{"offer URL takes precedence over SKU", "merchant.test", `<script type="application/ld+json">{"@type":["Thing","Product"],"name":"Gift","sku":"small","offers":[{"sku":"small","url":"/other","price":"29.90","priceCurrency":"EUR"},{"sku":"large","url":"/product","price":"97.30","priceCurrency":"EUR"}]}</script>`, "Gift", "97.3", "", false},
		{"selected SKU", "merchant.test", `<script type="application/ld+json">{"@graph":[{"@type":"http://schema.org/Product","name":"Gift","sku":"large","offers":[{"sku":"small","price":"29.90","priceCurrency":"EUR"},{"sku":"large","price":"94.00","priceCurrency":"EUR"}]}]}</script>`, "Gift", "94", "", false},
		{"unknown selected SKU cannot choose variants", "merchant.test", `<script type="application/ld+json">{"@type":"Product","name":"Gift","sku":"unknown","offers":[{"sku":"small","price":"29.90","priceCurrency":"EUR"},{"sku":"large","price":"94.00","priceCurrency":"EUR"}]}</script>`, "Gift", "", "", false},
		{"invalid offer prevents guessing", "merchant.test", `<meta property="product:price:amount" content="5"><meta property="product:price:currency" content="EUR"><script type="application/ld+json">{"@type":"Product","name":"Gift","offers":[{"price":"12","priceCurrency":"EUR"},{"price":"invalid","priceCurrency":"EUR"}]}</script>`, "Gift", "", "", false},
		{"foreign currency", "merchant.test", `<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"price":"12","priceCurrency":"USD"}}</script>`, "Gift", "", "", true},
		{"duplicate conflicting meta", "merchant.test", `<title>Fallback</title><meta property="OG:TITLE" content="First"><meta name="og:title" content="Second"><meta property="og:image" content="https://images.test/a"><meta property="og:image" content="https://images.test/b">`, "Fallback", "", "https://images.test/a", false},
		{"equivalent visible image", "merchant.test", `<script type="application/ld+json">{"@type":"Product","name":"Gift","image":[{"url":"image-id"}]}</script><img alt="image-id" src="https://images.test/full.jpg">`, "Gift", "", "https://images.test/full.jpg", false},
		{"ambiguous visible equivalent", "merchant.test", `<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"image-id"}</script><img alt="image-id" src="/a"><img alt="image-id" src="/b">`, "Gift", "", "https://merchant.test/image-id", false},
		{"challenge excludes site logo", "merchant.test", `<title>Just a moment</title><meta property="og:image" content="/logo"><div id="challenge-form"></div>`, "", "", "", false},
		{"challenge script", "merchant.test", `<title>Just a moment</title><script src="/cdn-cgi/challenge-platform/run"></script>`, "", "", "", false},
		{"genuine product with challenge library", "merchant.test", `<script>/cdn-cgi/challenge-platform/run</script><script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/photo","offers":{"price":"0.500","priceCurrency":"EUR"}}</script>`, "Gift", "0.5", "https://merchant.test/photo", false},
		{"raw JSON string control", "merchant.test", "<script type='application/ld+json'>{\"@type\":\"Product\",\"name\":\"Gift\",\"description\":\"line\nnext\"}</script>", "Gift", "", "", false},
		{"ignore nested recommendation graph", "merchant.test", `<title>Fallback</title><script type="application/ld+json">{"review":{"@type":"Product","name":"Unrelated"}}</script>`, "Fallback", "", "", false},
		{"microdata excludes nested scopes", "merchant.test", `<main itemscope itemtype="https://schema.org/Product" data-product-sku="large"><span itemscope itemtype="https://schema.org/Brand"><meta itemprop="name" content="Brand"></span><span itemprop="name"> Main   gift </span><img itemprop="image" src="/main.jpg"><div itemprop="offers" itemscope itemtype="http://schema.org/Offer"><meta itemprop="sku" content="small"><meta itemprop="price" content="29.9"><meta itemprop="priceCurrency" content="EUR"></div><div itemprop="offers" itemscope itemtype="https://schema.org/Offer"><meta itemprop="sku" content="large"><meta itemprop="price" content="94"><meta itemprop="priceCurrency" content="EUR"><span itemscope><meta itemprop="price" content="1"></span></div></main>`, "Main gift", "94", "https://merchant.test/main.jpg", false},
		{"microdata conflicting scalar", "merchant.test", `<main itemscope itemtype="http://schema.org/Product"><meta itemprop="name" content="First"><meta itemprop="name" content="Second"></main>`, "", "", "", false},
		{"structured overrides microdata", "merchant.test", `<main itemscope itemtype="http://schema.org/Product"><meta itemprop="name" content="Unrelated"></main><script type="application/ld+json">{"@type":"Product","name":"Chosen"}</script>`, "Chosen", "", "", false},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://" + test.host + "/product?variant=large")
			// Act
			result := importer.Extract([]byte(test.markup), base)
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
			if name != test.wantName || price != test.wantPrice || image != test.wantImage || result.UnsupportedCurrency != test.unsupported {
				t.Fatalf("suggestions differ: name %q, price %q, image %q, unsupported %v", name, price, image, result.UnsupportedCurrency)
			}
		})
	}
}

func TestExtract_WhenPriceHasUnsupportedPrecisionOrRange_DoesNotRound(t *testing.T) {
	for _, test := range []struct{ input, want string }{{".50", "0.5"}, {"12.", "12"}, {"00012.3000", "12.3"}, {"99999999.99", "99999999.99"}, {"100000000", ""}, {"12.301", ""}, {"0", ""}, {"-1", ""}, {"1e2", ""}, {"12,30", ""}} {
		t.Run(test.input, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product")
			markup := `<script type="application/ld+json">{"@type":"Product","offers":{"price":"` + test.input + `","priceCurrency":"EUR"}}</script>`
			// Act
			result := importer.Extract([]byte(markup), base)
			// Assert
			actual := ""
			if result.Price != nil {
				actual = result.Price.String()
			}
			if actual != test.want {
				t.Fatalf("price %q", actual)
			}
		})
	}
	base, _ := url.Parse("https://merchant.test/product")
	result := importer.Extract([]byte(`<meta property="og:title" content="`+strings.Repeat("a", 101)+`">`), base)
	if result.Name == nil || len(*result.Name) != 100 {
		t.Fatal("long suggestion did not preserve the public name limit")
	}
}

func TestExtract_WhenMerchantHostnameChanges_UsesTheSameStandardMetadata(t *testing.T) {
	for _, host := range []string{"www.amazon.fr", "www.fnac.com", "www.sephora.fr", "shop.independent-store.fr"} {
		t.Run(host, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://" + host + "/product")
			markup := `<script type="application/ld+json">{"@type":"Product","name":"Generic product","image":"/cover.jpg","offers":{"price":"25.50","priceCurrency":"EUR"}}</script>`
			// Act
			metadata := importer.Extract([]byte(markup), base)
			// Assert
			if metadata.Name == nil || *metadata.Name != "Generic product" || metadata.Price == nil || metadata.Price.String() != "25.5" || metadata.Image == nil || *metadata.Image != "https://"+host+"/cover.jpg" {
				t.Fatal("hostname changed standard extraction")
			}
		})
	}
}
