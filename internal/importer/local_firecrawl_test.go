package importer

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"strings"
	"testing"
	"time"
)

func firecrawlDocument(html, source, final string, origin int) *http.Response {
	body, _ := json.Marshal(map[string]any{"success": true, "data": map[string]any{"rawHtml": html, "metadata": map[string]any{"statusCode": origin, "sourceURL": source, "url": final}}})
	return relayResponse(200, string(body), "application/json")
}

func firecrawlFixture(t *testing.T, request providerRequest) *LocalFirecrawl {
	t.Helper()
	client := NewLocalFirecrawl(New(), "test-provider-key", func() time.Time { return time.Date(2026, 10, 27, 12, 0, 0, 0, time.UTC) })
	client.validator = localDirectFake{validate: func(ctx context.Context, _ string) error { return ctx.Err() }}
	client.request = request
	return client
}

func TestLocalFirecrawl_WhenProductRedirectsToStorefront_DoesNotImportStoreName(t *testing.T) {
	for _, productMode := range []bool{false, true} {
		t.Run(map[bool]string{false: "raw HTML", true: "product"}[productMode], func(t *testing.T) {
			// Arrange
			calls := 0
			client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				return firecrawlDocument(`<title>General storefront</title><meta property="og:image" content="/hero.jpg">`, "https://shop.merchant.fr/item", "https://shop.merchant.fr/", 200), nil
			})
			client.product = productMode
			target, _ := url.Parse("https://shop.merchant.fr/item")
			// Act
			result, err := client.Get(t.Context(), target)
			// Assert
			if err != nil || calls != 1 || result.Name != nil || result.Price != nil || result.Image != nil {
				t.Fatal("retired product became storefront merchandise or was retried")
			}
		})
	}
}

func TestLocalFirecrawl_WhenProductRedirectsToAnErrorPage_DoesNotImportErrorTitle(t *testing.T) {
	for _, tc := range []struct {
		name, final string
		wantName    bool
	}{
		{"unavailable page", "https://shop.merchant.fr/fr/page-indisponible", false},
		{"not found page", "https://shop.merchant.fr/not-found.html", false},
		{"localized unavailable product", "https://shop.merchant.fr/produit-introuvable", false},
		{"normal product redirect", "https://shop.merchant.fr/new-product", true},
		{"product slug contains a term", "https://shop.merchant.fr/product-unavailable-art-print", true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			calls := 0
			client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				return firecrawlDocument(`<title>Page indisponible | Store</title>`, "https://shop.merchant.fr/item", tc.final, 200), nil
			})
			target, _ := url.Parse("https://shop.merchant.fr/item")

			// Act
			result, err := client.Get(t.Context(), target)

			// Assert
			if err != nil || calls != 1 || (result.Name != nil) != tc.wantName || result.Price != nil || result.Image != nil {
				t.Fatal("redirected document was not handled as expected")
			}
		})
	}
}

func TestLocalFirecrawl_WhenDocumentReturned_AuthenticatesOnlyToFixedProviderAndParsesRawHTML(t *testing.T) {
	// Arrange
	target, _ := url.Parse("http://shop.merchant.fr:80/product#private")
	calls := 0
	client := firecrawlFixture(t, func(ctx context.Context, endpoint *url.URL, method string, body io.Reader, headers http.Header) (*http.Response, error) {
		calls++
		deadline, ok := ctx.Deadline()
		if endpoint.String() != localFirecrawlEndpoint || method != "POST" || headers.Get("Authorization") != "Bearer test-provider-key" || headers.Get("Cookie") != "" || !ok || time.Until(deadline) > localFirecrawlBudget {
			t.Fatal("provider boundary changed")
		}
		var input map[string]any
		if json.NewDecoder(body).Decode(&input) != nil || len(input) != 8 || input["url"] != "https://shop.merchant.fr/product" || input["onlyMainContent"] != false || input["proxy"] != "auto" || input["timeout"].(float64) > 12000 || input["maxAge"] != float64((47*time.Hour+49*time.Minute+50*time.Second).Milliseconds()) || input["skipTlsVerification"] != false {
			t.Fatal("raw HTML request changed")
		}
		location, ok := input["location"].(map[string]any)
		if !ok || location["country"] != "FR" {
			t.Fatal("French market setting lost")
		}
		languages, ok := location["languages"].([]any)
		if !ok || len(languages) != 2 || languages[0] != "fr-FR" || languages[1] != "fr" {
			t.Fatal("French language setting lost")
		}
		return firecrawlDocument(`<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/photo.jpg","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`, "https://shop.merchant.fr/product", "https://shop.merchant.fr/final", 200), nil
	})
	// Act
	metadata, err := client.Get(t.Context(), target)
	// Assert
	if err != nil || calls != 1 || metadata.Name == nil || *metadata.Name != "Gift" || metadata.Price == nil || metadata.Price.String() != "12.3" || metadata.Image == nil || *metadata.Image != "https://shop.merchant.fr/photo.jpg" {
		t.Fatal("provider response lost")
	}
}

func TestLocalFirecrawl_WhenProviderOrDocumentInvalid_RejectsWithoutRetryOrLeakingBody(t *testing.T) {
	for _, scenario := range []struct {
		name     string
		response func() *http.Response
		failure  error
		maximum  int
	}{
		{name: "network", failure: errors.New("secret-provider-content")},
		{name: "unauthorized", response: func() *http.Response { return relayResponse(401, "secret-provider-content", "application/json") }},
		{name: "credits", response: func() *http.Response { return relayResponse(402, "secret-provider-content", "application/json") }},
		{name: "forbidden", response: func() *http.Response { return relayResponse(403, "secret-provider-content", "application/json") }},
		{name: "redirect", response: func() *http.Response { return relayResponse(302, "secret-provider-content", "application/json") }},
		{name: "transient POST", response: func() *http.Response { return relayResponse(503, "secret-provider-content", "application/json") }},
		{name: "wrong media", response: func() *http.Response { return relayResponse(200, "secret-provider-content", "text/html") }},
		{name: "malformed", response: func() *http.Response { return relayResponse(200, "secret-provider-content", "application/json") }},
		{name: "merchant blocked", response: func() *http.Response { return firecrawlDocument("<title>secret-provider-content</title>", "", "", 403) }},
		{name: "missing origin", response: func() *http.Response { return firecrawlDocument("<title>secret-provider-content</title>", "", "", 0) }},
		{name: "empty HTML", response: func() *http.Response { return firecrawlDocument("", "", "", 200) }},
		{name: "HTML limit", maximum: 100, response: func() *http.Response { return firecrawlDocument(strings.Repeat("x", 101), "", "", 200) }},
		{name: "envelope limit", maximum: 10, response: func() *http.Response { return relayResponse(200, strings.Repeat("x", 61), "application/json") }},
		{name: "private final", response: func() *http.Response {
			return firecrawlDocument("<title>Gift</title>", "https://merchant.fr/p", "https://127.0.0.1/", 200)
		}},
		{name: "private source", response: func() *http.Response {
			return firecrawlDocument("<title>Gift</title>", "https://metadata.google.internal/p", "", 200)
		}},
		{name: "invalid final", response: func() *http.Response { return firecrawlDocument("<title>Gift</title>", "", "https://bad%zz/", 200) }},
		{name: "HTTP final", response: func() *http.Response { return firecrawlDocument("<title>Gift</title>", "", "http://merchant.fr/", 200) }},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			calls := 0
			client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				if scenario.failure != nil {
					return nil, scenario.failure
				}
				return scenario.response(), nil
			})
			client.maximumBytes = scenario.maximum
			target, _ := url.Parse("https://merchant.fr/product")
			// Act
			metadata, err := client.Get(t.Context(), target)
			// Assert
			if err == nil || strings.Contains(err.Error(), "secret-provider-content") || metadata.Name != nil || calls != 1 {
				t.Fatal("invalid response accepted, retried or exposed")
			}
		})
	}
}

func TestLocalFirecrawl_WhenURLOrDNSRejected_DoesNotContactProvider(t *testing.T) {
	for _, value := range []string{"https://127.0.0.1/", "https://merchant.fr:8443/", "https://user@merchant.fr/", "https://merchant.fr/"} {
		t.Run(value, func(t *testing.T) {
			// Arrange
			client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				t.Fatal("provider contacted after rejected URL")
				return nil, nil
			})
			client.validator = localDirectFake{validate: func(context.Context, string) error { return rejected() }}
			target, _ := url.Parse(value)
			// Act / Assert
			if _, err := client.Get(t.Context(), target); err == nil {
				t.Fatal("rejected URL accepted")
			}
		})
	}
}

func TestLocalFirecrawl_WhenRateOrAccountLimitReached_HonorsCooldownWithControlledClock(t *testing.T) {
	for _, scenario := range []struct {
		status   int
		retry    string
		duration time.Duration
	}{
		{429, "120", 120 * time.Second}, {429, "Tue, 27 Oct 2026 12:02:00 GMT", 120 * time.Second}, {429, "bad", time.Minute}, {401, "", 24 * time.Hour}, {402, "", 24 * time.Hour}, {403, "", 24 * time.Hour},
	} {
		t.Run(http.StatusText(scenario.status)+scenario.retry, func(t *testing.T) {
			// Arrange
			calls := 0
			client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				response := relayResponse(scenario.status, "", "application/json")
				response.Header.Set("Retry-After", scenario.retry)
				return response, nil
			})
			now := client.now()
			client.now = func() time.Time { return now }
			target, _ := url.Parse("https://merchant.fr/product")
			// Act
			_, first := client.Get(t.Context(), target)
			now = now.Add(scenario.duration - time.Millisecond)
			_, second := client.Get(t.Context(), target)
			// Assert
			if first == nil || second == nil || calls != 1 {
				t.Fatal("provider quota was retried")
			}
			if scenario.status == 429 {
				now = now.Add(time.Millisecond)
				_, _ = client.Get(t.Context(), target)
				if calls != 2 {
					t.Fatal("provider cooldown did not expire")
				}
			}
		})
	}
}

func TestLocalFirecrawl_WhenSequentialRequestsArrive_SpacesStartsWithoutSleepingInTest(t *testing.T) {
	// Arrange
	client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		return firecrawlDocument("<title>Gift</title>", "", "", 200), nil
	})
	var waits []time.Duration
	client.wait = func(ctx context.Context, delay time.Duration) error {
		if _, ok := ctx.Deadline(); !ok {
			t.Fatal("wait lost request budget")
		}
		waits = append(waits, delay)
		return nil
	}
	target, _ := url.Parse("https://merchant.fr/product")
	// Act
	_, first := client.Get(t.Context(), target)
	_, second := client.Get(t.Context(), target)
	// Assert
	if first != nil || second != nil || len(waits) != 1 || waits[0] != firecrawlStartInterval {
		t.Fatal("free plan pacing changed")
	}
}

func TestLocalFirecrawl_WhenContextCanceledOrBudgetTooShort_SkipsProvider(t *testing.T) {
	// Arrange
	client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		t.Fatal("provider called without usable budget")
		return nil, nil
	})
	target, _ := url.Parse("https://merchant.fr/product")
	canceled, cancel := context.WithCancel(t.Context())
	cancel()
	short, finish := context.WithTimeout(t.Context(), time.Second)
	defer finish()
	// Act / Assert
	if _, err := client.Get(canceled, target); !errors.Is(err, context.Canceled) {
		t.Fatal("cancellation lost")
	}
	if _, err := client.Get(short, target); err == nil {
		t.Fatal("image reserve lost")
	}
}

func TestLocalFirecrawl_WhenAnotherRequestHitsQuotaDuringWait_DoesNotSendQueuedRequest(t *testing.T) {
	// Arrange
	calls := 0
	client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		calls++
		return firecrawlDocument("<title>Gift</title>", "", "", 200), nil
	})
	client.nextStart = client.now().Add(time.Second)
	client.wait = func(context.Context, time.Duration) error { client.block(429, "120"); return nil }
	target, _ := url.Parse("https://merchant.fr/product")
	// Act
	_, err := client.Get(t.Context(), target)
	// Assert
	if err == nil || calls != 0 {
		t.Fatal("queued request ignored account cooldown")
	}
}

type pipelineResolver struct{}

func (pipelineResolver) LookupNetIP(ctx context.Context, _, _ string) ([]netip.Addr, error) {
	return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, ctx.Err()
}

func TestPreview_WhenCloudflareTransientAttemptsFail_UsesFirecrawlLastWithoutDirectPageRequest(t *testing.T) {
	// Arrange
	client := New()
	client.Resolver = pipelineResolver{}
	var order []string
	primary := NewLocalCloudflare(client, "test")
	primary.wait = func(ctx context.Context, _ time.Duration) error { return ctx.Err() }
	primary.http = &http.Client{Transport: localRelayTransport(func(*http.Request) (*http.Response, error) {
		order = append(order, "cloudflare")
		return relayResponse(503, "", "text/html"), nil
	})}
	final := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		order = append(order, "firecrawl")
		return firecrawlDocument(`<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`, "", "", 200), nil
	})
	client.PageSource, client.FallbackSource = primary, final
	// Act
	preview, err := client.Preview(t.Context(), "https://merchant.fr/product")
	// Assert
	if err != nil || strings.Join(order, ",") != "cloudflare,cloudflare,firecrawl" || preview.Name == nil || preview.Price == nil {
		t.Fatal("provider sequence or retained suggestions changed")
	}
}
