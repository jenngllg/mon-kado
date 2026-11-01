package importer_test

import (
	"bytes"
	"context"
	"image"
	"image/png"
	"net/netip"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestPreview_WhenProvidersIdentifyDifferentProducts_DoesNotMixSuggestions(t *testing.T) {
	const first = `{"@type":"Product","name":"First gift","gtin13":"6291041500213"}`
	const same = `{"@type":"Product","name":"Translated gift","gtin14":"06291041500213","offers":{"price":"42","priceCurrency":"EUR"}}`
	const different = `{"@type":"Product","name":"Other gift","gtin14":"17841945768163","offers":{"price":"42","priceCurrency":"EUR"}}`
	const unknown = `{"@type":"Product","name":"Gift","offers":{"price":"42","priceCurrency":"EUR"}}`
	for _, tc := range []struct {
		name, renderer, fallback string
		wantPrice                bool
	}{
		{"conflicting fallback", "", different, false},
		{"matching fallback despite different title", "", same, true},
		{"fallback without identifier", "", unknown, true},
		{"conflicting renderer and fallback", different, different, false},
		{"conflicting renderer then matching fallback", different, same, true},
		{"matching renderer", same, different, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product")
			metadata := func(product string) importer.Metadata {
				return importer.Extract([]byte(`<script type="application/ld+json">`+product+`</script>`), base)
			}
			var picture bytes.Buffer
			if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
				t.Fatal(err)
			}
			primary := metadata(first)
			primary.ImageData = picture.Bytes()
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) { return primary, nil }}
			if tc.renderer != "" {
				client.RenderedSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) { return metadata(tc.renderer), nil }}
			}
			client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) { return metadata(tc.fallback), nil }}

			// Act
			result, err := client.Preview(t.Context(), base.String())

			// Assert
			if err != nil || result.Name == nil || *result.Name != "First gift" || result.Image == nil || (result.Price != nil) != tc.wantPrice {
				t.Fatalf("unexpected merged preview: name=%v image=%v price=%v error=%v", result.Name, result.Image != nil, result.Price, err)
			}
		})
	}
}
