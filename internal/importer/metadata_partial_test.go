package importer_test

import (
	"github.com/JennGllg/mon-kado/internal/importer"
	"net/url"
	"testing"
)

func TestExtract_WhenPriceUsesAlternativeMetadata_RequiresUnambiguousEuroAmount(t *testing.T) {
	for _, scenario := range []struct {
		name, html, price string
		unsupported       bool
	}{
		{"og price", `<meta property="og:price:amount" content="59.00"><meta property="og:price:currency" content="EUR">`, "59", false},
		{"decimal comma", `<meta property="og:price:amount" content="59,50"><meta property="og:price:currency" content="EUR">`, "59.5", false},
		{"ambiguous thousands", `<meta property="og:price:amount" content="1,000"><meta property="og:price:currency" content="EUR">`, "", false},
		{"mixed separators", `<meta property="og:price:amount" content="1.000,50"><meta property="og:price:currency" content="EUR">`, "", false},
		{"foreign currency", `<meta property="og:price:amount" content="59.00"><meta property="og:price:currency" content="USD">`, "", true},
		{"missing currency", `<meta property="og:price:amount" content="59.00">`, "", true},
		{"consistent aliases", `<meta property="product:price:amount" content="59.0"><meta property="product:price:currency" content="EUR"><meta property="og:price:amount" content="59.00"><meta property="og:price:currency" content="EUR">`, "59", false},
		{"conflicting aliases", `<meta property="product:price:amount" content="59.0"><meta property="product:price:currency" content="EUR"><meta property="og:price:amount" content="69.00"><meta property="og:price:currency" content="EUR">`, "", false},
		{"structured conflict preserved", `<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":[{"price":"59","priceCurrency":"EUR"},{"price":"69","priceCurrency":"EUR"}]}</script><meta property="og:price:amount" content="59.00"><meta property="og:price:currency" content="EUR">`, "", false},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.example/product")
			// Act
			metadata := importer.Extract([]byte(scenario.html), base)
			// Assert
			actual := ""
			if metadata.Price != nil {
				actual = metadata.Price.String()
			}
			if actual != scenario.price || metadata.UnsupportedCurrency != scenario.unsupported {
				t.Fatal("price metadata currency or ambiguity rule changed")
			}
		})
	}
}

func TestExtract_WhenEquivalentProductAndGroupShareIdentity_RetainsVariantPriceRules(t *testing.T) {
	for _, scenario := range []struct{ name, secondPrice, expected string }{
		{"same amount", "46.00", "46"}, {"different amounts", "59.00", ""},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.example/product")
			content := `<script type="application/ld+json">[{"@type":"Product","@id":"/product#product","name":"Gift","url":"/product"},{"@type":"ProductGroup","@id":"/product#product","name":"Gift","url":"/product","hasVariant":[{"@type":"Product","offers":{"price":"46.00","priceCurrency":"EUR"}},{"@type":"Product","offers":{"price":"` + scenario.secondPrice + `","priceCurrency":"EUR"}}]}]</script><meta property="og:price:amount" content="46,00"><meta property="og:price:currency" content="EUR">`
			// Act
			metadata := importer.Extract([]byte(content), base)
			// Assert
			actual := ""
			if metadata.Price != nil {
				actual = metadata.Price.String()
			}
			if actual != scenario.expected {
				t.Fatal("equivalent group lost its variants or ambiguity guard")
			}
		})
	}
}

func TestExtract_WhenPageIsExplicitBlockMessage_DoesNotSuggestErrorHeadingAsProduct(t *testing.T) {
	for _, heading := range []string{"You have been blocked", "Access denied", "Request blocked!", "Request rejected"} {
		t.Run(heading, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.example/product")
			// Act
			metadata := importer.Extract([]byte("<h1>"+heading+"</h1>"), base)
			// Assert
			if metadata.Name != nil || metadata.Price != nil || metadata.Image != nil {
				t.Fatal("block page suggested as product")
			}
		})
	}
}

func TestExtract_WhenRealProductContainsBlockWords_PreservesStructuredProduct(t *testing.T) {
	// Arrange
	base, _ := url.Parse("https://merchant.example/product")
	// Act
	metadata := importer.Extract([]byte(`<title>Access denied</title><script type="application/ld+json">{"@type":"Product","name":"Access denied","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base)
	// Assert
	if metadata.Name == nil || metadata.Price == nil {
		t.Fatal("genuine product was rejected by incidental block words")
	}
}
