package importer

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/url"
	"strings"
	"testing"
)

func productDocument(markup, product string) *http.Response {
	body, _ := json.Marshal(map[string]any{"success": true, "data": map[string]any{"rawHtml": markup, "product": json.RawMessage(product), "metadata": map[string]any{"statusCode": 200, "url": "https://merchant.fr/product"}}})
	return relayResponse(200, string(body), "application/json")
}

func TestLocalFirecrawlProduct_WhenSuggestionsDiffer_OnlyCompletesUnambiguousSameProduct(t *testing.T) {
	standard := `{"title":"Gift","url":"https://merchant.fr/product","variants":[{"price":{"amount":19.90,"currency":"EUR"},"images":[{"url":"/photo.png"}]}]}`
	for _, tc := range []struct{ name, markup, product, url, wantName, wantPrice, wantImage string }{
		{"product supplies missing fields", `<main>Gift</main>`, standard, "", "Gift", "19.9", "https://merchant.fr/photo.png"},
		{"raw HTML remains primary", `<title>Original</title><meta property="product:price:amount" content="10"><meta property="product:price:currency" content="EUR"><meta property="og:image" content="/original.jpg">`, standard, "", "Original", "10", "https://merchant.fr/original.jpg"},
		{"ambiguous original offers", `<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":[{"price":"10","priceCurrency":"EUR"},{"price":"20","priceCurrency":"EUR"}]}</script>`, standard, "", "Gift", "", "https://merchant.fr/photo.png"},
		{"conflicting page metadata", `<title>Gift</title><meta property="product:price:amount" content="10"><meta property="product:price:currency" content="EUR"><meta property="og:price:amount" content="20"><meta property="og:price:currency" content="EUR">`, standard, "", "Gift", "", "https://merchant.fr/photo.png"},
		{"different product", `<title>Original</title>`, strings.Replace(standard, "merchant.fr/product", "merchant.fr/other", 1), "", "Original", "", ""},
		{"unknown variant query", `<title>Gift</title>`, standard, "https://merchant.fr/product?variant=other", "Gift", "", ""},
		{"tracking query", `<title>Gift</title>`, standard, "https://merchant.fr/product?utm_source=share", "Gift", "19.9", "https://merchant.fr/photo.png"},
		{"foreign currency", `<title>Gift</title>`, strings.Replace(standard, "EUR", "USD", 1), "", "Gift", "", "https://merchant.fr/photo.png"},
		{"zero is not price", `<title>Gift</title>`, strings.Replace(standard, "19.90", "0", 1), "", "Gift", "", "https://merchant.fr/photo.png"},
		{"exponent is not normalized money", `<title>Gift</title>`, strings.Replace(standard, "19.90", "1.99e1", 1), "", "Gift", "", "https://merchant.fr/photo.png"},
		{"missing product", `<title>Gift</title>`, `null`, "", "Gift", "", ""},
		{"wrong field shape", `<title>Gift</title>`, `{"title":"Gift","url":"https://merchant.fr/product","variants":"bad"}`, "", "Gift", "", ""},
		{"no variants", `<title>Gift</title>`, `{"title":"Gift","url":"https://merchant.fr/product","variants":[]}`, "", "Gift", "", ""},
		{"blocked page", `<title>You have been blocked</title>`, standard, "", "", "", ""},
		{"unanimous variant prices", `<title>Gift</title>`, `{"title":"Gift","url":"https://merchant.fr/product","variants":[{"price":{"amount":20,"currency":"EUR"},"images":[{"url":"/red.jpg"}]},{"price":{"amount":20.00,"currency":"EUR"},"images":[{"url":"/blue.jpg"}]}]}`, "", "Gift", "20", ""},
		{"different variant prices", `<title>Gift</title>`, `{"title":"Gift","url":"https://merchant.fr/product","variants":[{"price":{"amount":20,"currency":"EUR"}},{"price":{"amount":30,"currency":"EUR"}}]}`, "", "Gift", "", ""},
		{"incomplete variant prices", `<title>Gift</title>`, `{"title":"Gift","url":"https://merchant.fr/product","variants":[{}, {"price":{"amount":30,"currency":"EUR"}}]}`, "", "Gift", "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			calls := 0
			client := firecrawlFixture(t, func(ctx context.Context, endpoint *url.URL, method string, body io.Reader, headers http.Header) (*http.Response, error) {
				calls++
				var request firecrawlRequest
				if json.NewDecoder(body).Decode(&request) != nil || strings.Join(request.Formats, ",") != "rawHtml,product" {
					t.Fatal("product request changed")
				}
				return productDocument(tc.markup, tc.product), nil
			})
			client.product = true
			address := tc.url
			if address == "" {
				address = "https://merchant.fr/product"
			}
			target, _ := url.Parse(address)
			// Act
			r, err := client.Get(t.Context(), target)
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
			if err != nil || calls != 1 || n != tc.wantName || p != tc.wantPrice || i != tc.wantImage {
				t.Fatalf("got %q %q %q calls %d error %v", n, p, i, calls, err)
			}
		})
	}
}
