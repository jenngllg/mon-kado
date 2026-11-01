package importer_test

import (
	"context"
	"github.com/JennGllg/mon-kado/internal/importer"
	"net/netip"
	"net/url"
	"testing"
)

func TestPublicAddress_WhenDestinationIsSpecial_RejectsIPv4IPv6AndMappedForms(t *testing.T) {
	// Arrange
	for _, value := range []string{"127.0.0.1", "10.1.2.3", "169.254.169.254", "100.64.0.1", "192.0.2.1", "198.18.1.1", "203.0.113.1", "::1", "fc00::1", "fe80::1", "::ffff:127.0.0.1", "2001:db8::1", "2002::1", "3fff::1"} {
		t.Run(value, func(t *testing.T) {
			// Act
			allowed := importer.PublicAddress(netip.MustParseAddr(value))
			// Assert
			if allowed {
				t.Fatal("special-purpose address permitted")
			}
		})
	}
}

type resolverFake struct{ Addresses []netip.Addr }

func (r resolverFake) LookupNetIP(ctx context.Context, network, host string) ([]netip.Addr, error) {
	return r.Addresses, ctx.Err()
}

func TestValidate_WhenDNSMixesPublicAndPrivate_RejectsEntireAddressSet(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8"), netip.MustParseAddr("10.0.0.1")}}
	// Act
	err := client.Validate(t.Context(), "https://merchant.example/product")
	// Assert
	if err == nil {
		t.Fatal("mixed DNS answer permitted")
	}
}

func TestExtract_WhenJSONLDContainsProduct_PrefersProductAndRejectsConflictingPrices(t *testing.T) {
	// Arrange
	base, _ := url.Parse("https://merchant.example/products/gift")
	content := []byte(`<html><title>Fallback</title><meta property="og:title" content="Other title"><script type="application/ld+json">{"@graph":[{"@type":"Product","name":"A gift","image":"/image.png","offers":[{"price":"12.30","priceCurrency":"EUR"},{"price":12.3,"priceCurrency":"EUR"}]}]}</script></html>`)
	// Act
	result := importer.Extract(content, base)
	ambiguous := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","offers":[{"price":12,"priceCurrency":"EUR"},{"price":13,"priceCurrency":"EUR"}]}</script>`), base)
	// Assert
	if result.Name == nil || *result.Name != "A gift" || result.Price == nil || result.Price.String() != "12.3" || result.Image == nil || *result.Image != "https://merchant.example/image.png" || ambiguous.Price != nil {
		t.Fatal("metadata extraction invariant violated")
	}
}
