package importer

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/url"
	"testing"
	"time"
)

func TestLocalFirecrawl_WhenPreviewBudgetShortened_ReservesImagesAndDoesNotRetry(t *testing.T) {
	for _, tc := range []struct {
		name            string
		total, expected time.Duration
		htmlOnly        bool
	}{
		{"default", 8 * time.Second, 6 * time.Second, false},
		{"reduced total", 3 * time.Second, 2 * time.Second, false},
		{"old extended caller still bounded", 35 * time.Second, 6 * time.Second, false},
		{"defensive maximum", time.Minute, 6 * time.Second, false},
		{"HTML shares the short budget", 8 * time.Second, 6 * time.Second, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			direct := New()
			direct.Timeout = tc.total
			provider := NewLocalFirecrawlProduct(direct, "test-provider-key", time.Now)
			provider.validator = localDirectFake{validate: func(ctx context.Context, _ string) error { return ctx.Err() }}
			calls := 0
			provider.request = func(ctx context.Context, endpoint *url.URL, method string, body io.Reader, _ http.Header) (*http.Response, error) {
				calls++
				var input struct {
					Timeout int64    `json:"timeout"`
					Formats []string `json:"formats"`
				}
				if json.NewDecoder(body).Decode(&input) != nil || method != "POST" || endpoint.String() != localFirecrawlEndpoint {
					t.Fatal("provider boundary changed")
				}
				if input.Timeout > tc.expected.Milliseconds() || input.Timeout < (tc.expected-3*time.Second).Milliseconds() {
					t.Fatalf("unexpected provider timeout: %d", input.Timeout)
				}
				if len(input.Formats) < 1 || input.Formats[0] != "rawHtml" || (len(input.Formats) == 1) != tc.htmlOnly {
					t.Fatal("per-request format choice was not preserved")
				}
				return firecrawlDocument(`<script type="application/ld+json">{"@type":"Product","name":"Notebook","offers":{"price":"10","priceCurrency":"EUR"}}</script>`, "", "", 200), nil
			}
			target, _ := url.Parse("https://shop.merchant.fr/item")
			// Act
			var result Metadata
			var err error
			if tc.htmlOnly {
				result, err = provider.GetHTML(t.Context(), target)
			} else {
				result, err = provider.Get(t.Context(), target)
			}
			// Assert
			if err != nil || calls != 1 || result.Price == nil || result.Price.String() != "10" || !provider.product {
				t.Fatalf("extended retrieval failed or retried: %v", err)
			}
		})
	}
}
