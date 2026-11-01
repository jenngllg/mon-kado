package importer_test

import (
	"io"
	"net/http"
	"net/netip"
	"slices"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestPreview_WhenMerchantPageExceedsFormerLimit_PreservesSuggestionsWithinBound(t *testing.T) {
	for _, test := range []struct {
		name       string
		bytes      int
		zeroOption bool
		accepted   bool
	}{
		{"above former limit", 2*1024*1024 + 100, false, true},
		{"zero option uses same default", 2*1024*1024 + 100, true, true},
		{"exact upper boundary", config.MaximumImportHTMLBytes, false, true},
		{"above upper boundary", config.MaximumImportHTMLBytes + 1, false, false},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			if test.zeroOption {
				client.MaximumHTMLBytes = 0
			}
			markup := `<html><script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"price":"12.30","priceCurrency":"EUR"}}</script></html>`
			body := markup + strings.Repeat(" ", test.bytes-len(markup))
			calls := pipeMerchant(t, client, func(req *http.Request) *http.Response {
				if req.URL.RawQuery != "variant=large&ref_=saved" {
					t.Error("product selection or referral parameters were changed")
				}
				return &http.Response{StatusCode: http.StatusOK, Header: http.Header{"Content-Type": {"text/html"}}, Body: io.NopCloser(strings.NewReader(body))}
			})

			// Act
			preview, err := client.Preview(t.Context(), "http://merchant.test/product?variant=large&ref_=saved")

			// Assert
			if err != nil || *calls != 1 {
				t.Fatalf("preview failed or retried: %v, requests %d", err, *calls)
			}
			if test.accepted {
				if preview.Name == nil || *preview.Name != "Gift" || preview.Price == nil || preview.Price.String() != "12.3" || slices.Contains(preview.Warnings, "WISH_IMPORT_PAGE_UNAVAILABLE") {
					t.Fatal("bounded large-page suggestions were discarded")
				}
			} else if preview.Name != nil || preview.Price != nil || !slices.Contains(preview.Warnings, "WISH_IMPORT_PAGE_UNAVAILABLE") {
				t.Fatal("oversized response was accepted or partial data leaked")
			}
		})
	}
}
