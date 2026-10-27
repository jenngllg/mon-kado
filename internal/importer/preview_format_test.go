package importer_test

import (
	"context"
	"net/netip"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

type formatFallback struct {
	productCalls, htmlCalls int
	metadata                importer.Metadata
}

func (s *formatFallback) CanHandle(*url.URL) bool { return true }
func (s *formatFallback) Get(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
	s.productCalls++
	return s.metadata, ctx.Err()
}
func (s *formatFallback) GetHTML(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
	s.htmlCalls++
	return s.metadata, ctx.Err()
}

func TestPreview_WhenPrimaryIsEmptyOrPartial_SelectsOneFallbackFormat(t *testing.T) {
	for _, empty := range []bool{false, true} {
		t.Run(map[bool]string{false: "partial primary", true: "empty primary"}[empty], func(t *testing.T) {
			// Arrange
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			base, _ := url.Parse("https://merchant.fr/item")
			metadata := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Notebook","offers":{"price":"10","priceCurrency":"EUR"}}</script>`), base)
			primary := metadata
			if empty {
				primary = importer.Metadata{}
			} else {
				primary.Price = nil
			}
			client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) { return primary, nil }}
			fallback := &formatFallback{metadata: metadata}
			client.FallbackSource = fallback
			// Act
			result, err := client.Preview(t.Context(), base.String())
			// Assert
			if err != nil || result.Name == nil || result.Price == nil || result.Price.String() != "10" || fallback.htmlCalls+fallback.productCalls != 1 {
				t.Fatal("fallback lost metadata or called the provider more than once")
			}
			if (fallback.htmlCalls == 1) != empty {
				t.Fatal("wrong fallback format selected")
			}
		})
	}
}
