package importer_test

import (
	"context"
	"net/netip"
	"net/url"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestPreview_WhenFallbackDeadlineExceeded_RetainsPrimaryTextWithinShortBudget(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	base, _ := url.Parse("https://merchant.test/product")
	metadata := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Notebook","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base)
	client.PageSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) { return metadata, ctx.Err() }}
	calls := 0
	client.FallbackSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
		calls++
		deadline, ok := ctx.Deadline()
		if !ok || time.Until(deadline) > config.MaximumImportDuration {
			t.Fatal("preview deadline exceeds the short server budget")
		}
		return importer.Metadata{}, context.DeadlineExceeded
	}}
	// Act
	result, err := client.Preview(t.Context(), base.String())
	// Assert
	if err != nil || calls != 1 || result.Name == nil || *result.Name != "Notebook" || result.Price == nil || result.Price.String() != "12.3" || result.Image != nil {
		t.Fatal("provider timeout lost available primary fields or retried")
	}
}
