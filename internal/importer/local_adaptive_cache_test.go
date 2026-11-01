package importer

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"image"
	"image/png"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestPreviewCache_WhenLocalImagesAreLarge_StoresBinaryWithinBoundAndPreservesResponse(t *testing.T) {
	// Arrange
	now := time.Date(2026, time.October, 29, 0, 0, 0, 0, time.UTC)
	cache := NewWithAdaptivePreviewCache(func() time.Time { return now }).cache
	image := base64.StdEncoding.EncodeToString(bytes.Repeat([]byte{0x7e}, 6*1024*1024+512*1024))
	name, amount := "Product", json.Number("12.50")
	preview := domain.WishImportPreview{Name: &name, Price: &amount, Image: &domain.WishImportImage{ContentType: "image/webp", ContentBase64: image}, Warnings: []string{}}
	first, second, third := cacheKey("https://merchant.test/one"), cacheKey("https://merchant.test/two"), cacheKey("https://merchant.test/three")
	if 2*len(image) <= maximumPreviewCacheBytes {
		t.Fatal("fixture must exceed the old encoded-byte budget")
	}
	// Act
	cache.put(first, preview, false, now.Add(maximumPriceFreshness))
	cache.put(second, preview, false, now.Add(maximumPriceFreshness))
	for _, key := range []previewKey{first, second} {
		result, hit, release, err := cache.acquire(t.Context(), key)
		if release != nil {
			release()
		}
		if err != nil || !hit || result.Image == nil || result.Image.ContentBase64 != image {
			t.Fatal("large cached image was evicted or changed")
		}
	}
	cache.put(third, preview, false, now.Add(maximumPriceFreshness))
	_, hit, release, err := cache.acquire(t.Context(), first)
	if release != nil {
		release()
	}
	// Assert
	if err != nil || hit || cache.bytes > maximumPreviewCacheBytes || len(cache.entries) != 2 {
		t.Fatal("binary cache failed to evict the least recently used entry within its bound")
	}
}

type adaptiveTestImage struct{ content []byte }

func (s adaptiveTestImage) GetImage(ctx context.Context, _ *url.URL) ([]byte, error) {
	return s.content, ctx.Err()
}

func TestPreview_WhenLocalProviderReportsCacheAge_ExpiresWithinPriceFreshnessLimit(t *testing.T) {
	for _, tc := range []struct {
		name, state            string
		age, ttl               time.Duration
		invalidDate            bool
		primaryPrice, standard bool
	}{
		{"fresh scrape", "miss", 0, 48*time.Hour - 10*time.Second, false, false, false},
		{"cached for twelve hours", "hit", 12 * time.Hour, 36*time.Hour - 10*time.Second, false, false, false},
		{"near the freshness limit", "hit", 47*time.Hour + 55*time.Minute, 5*time.Minute - 10*time.Second, false, false, false},
		{"unknown state", "", 0, 10 * time.Minute, false, false, false},
		{"missing timestamp", "hit", 0, 10 * time.Minute, true, false, false},
		{"future timestamp", "hit", -time.Minute, 10 * time.Minute, false, false, false},
		{"price from another source", "miss", 0, 48*time.Hour - 10*time.Second, false, true, false},
		{"standard cache remains ten minutes", "miss", 0, 10 * time.Minute, false, false, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			now := time.Date(2026, 10, 28, 12, 0, 0, 0, time.UTC)
			initial := now
			calls := 0
			provider := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				stamp := initial.Add(-tc.age).Format(time.RFC3339Nano)
				if tc.invalidDate {
					stamp = ""
				}
				metadata := map[string]any{"statusCode": 200, "url": "https://merchant.fr/product", "cacheState": tc.state, "cachedAt": stamp}
				html := `<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"https://merchant.fr/photo","offers":{"price":"12","priceCurrency":"EUR"}}</script>`
				body, _ := json.Marshal(map[string]any{"success": true, "data": map[string]any{"rawHtml": html, "metadata": metadata}})
				return relayResponse(200, string(body), "application/json"), nil
			})
			provider.now = func() time.Time { return now }
			var picture bytes.Buffer
			if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
				t.Fatal(err)
			}
			client := NewWithAdaptivePreviewCache(func() time.Time { return now })
			if tc.standard {
				client = NewWithPreviewCache(func() time.Time { return now })
			}
			client.Resolver = &previewTestResolver{answers: [][]netip.Addr{{netip.MustParseAddr("1.1.1.1")}}}
			client.Dialer = previewTestUnavailableDialer{}
			client.PageSource = &previewTestPage{err: errors.New("unavailable")}
			if tc.primaryPrice {
				price := json.Number("12")
				client.PageSource = &previewTestPage{metadata: Metadata{Price: &price}}
			}
			client.FallbackSource = provider
			client.ImageSource = adaptiveTestImage{content: picture.Bytes()}

			// Act
			first, err := client.Preview(t.Context(), "https://merchant.fr/product")
			if err != nil || first.Price == nil || first.Image == nil || calls != 1 {
				t.Fatalf("initial preview unavailable: %v", err)
			}
			wantWarning := !tc.primaryPrice && !tc.standard
			if (len(first.Warnings) == 1 && first.Warnings[0] == domain.WishImportPriceMayBeStale) != wantWarning {
				t.Fatalf("unexpected price freshness warning: %v", first.Warnings)
			}
			if tc.primaryPrice && !tc.standard {
				now = initial.Add(10*time.Minute - time.Second)
				fresh, freshErr := client.Preview(t.Context(), "https://merchant.fr/product")
				if freshErr != nil || len(fresh.Warnings) != 0 || calls != 1 {
					t.Fatal("recent primary price was not reused without a stale warning")
				}
				now = initial.Add(10 * time.Minute)
				older, olderErr := client.Preview(t.Context(), "https://merchant.fr/product")
				if olderErr != nil || len(older.Warnings) != 1 || older.Warnings[0] != domain.WishImportPriceMayBeStale || calls != 1 {
					t.Fatal("older primary price was not reused with a stale warning")
				}
			}
			now = initial.Add(tc.ttl - time.Second)
			cached, err := client.Preview(t.Context(), "https://merchant.fr/product")
			if err != nil || calls != 1 {
				t.Fatal("fresh preview was fetched again before expiry")
			}
			if (len(cached.Warnings) == 1 && cached.Warnings[0] == domain.WishImportPriceMayBeStale) != (wantWarning || tc.primaryPrice && !tc.standard) {
				t.Fatalf("cached price freshness warning was lost: %v", cached.Warnings)
			}
			now = initial.Add(tc.ttl)
			_, err = client.Preview(t.Context(), "https://merchant.fr/product")

			// Assert
			if err != nil || calls != 2 {
				t.Fatal("price outlived its permitted cache age")
			}
		})
	}
}

func TestLocalFirecrawl_WhenReportedCachedPageIsOlderThanLimit_RejectsIt(t *testing.T) {
	// Arrange
	provider := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		body, _ := json.Marshal(map[string]any{"success": true, "data": map[string]any{"rawHtml": `<title>Gift</title>`, "metadata": map[string]any{"statusCode": 200, "cacheState": "hit", "cachedAt": "2026-10-25T12:00:00Z"}}})
		return relayResponse(200, string(body), "application/json"), nil
	})
	target, _ := url.Parse("https://merchant.fr/product")

	// Act
	metadata, err := provider.Get(t.Context(), target)

	// Assert
	if err == nil || metadata.Name != nil || metadata.Price != nil {
		t.Fatal("stale provider data was returned")
	}
}

func TestPreview_WhenLocalBrowserSuppliesPrice_ReusesItWithBoundedAge(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 28, 12, 0, 0, 0, time.UTC)
	initial := now
	var picture bytes.Buffer
	if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	name, price := "Gift", json.Number("12")
	primary := &previewTestPage{err: errors.New("unavailable")}
	rendered := &previewTestPage{metadata: Metadata{Name: &name, Price: &price, ImageData: picture.Bytes()}}
	fallback := &previewTestPage{err: errors.New("should not be called")}
	client := NewWithAdaptivePreviewCache(func() time.Time { return now })
	client.Resolver = &previewTestResolver{answers: [][]netip.Addr{{netip.MustParseAddr("1.1.1.1")}}}
	client.PageSource, client.RenderedSource, client.FallbackSource = primary, rendered, fallback

	// Act
	first, err := client.Preview(t.Context(), "https://merchant.fr/product")
	if err != nil || first.Price == nil || first.Image == nil || len(first.Warnings) != 0 {
		t.Fatal("browser preview was unavailable")
	}
	now = initial.Add(47 * time.Hour)
	cached, err := client.Preview(t.Context(), "https://merchant.fr/product")

	// Assert
	if err != nil || cached.Price == nil || len(cached.Warnings) != 1 || cached.Warnings[0] != domain.WishImportPriceMayBeStale || rendered.calls != 1 || fallback.calls != 0 {
		t.Fatal("browser result was not reused with a freshness warning")
	}
	now = initial.Add(maximumPriceFreshness - providerFreshnessSafety)
	_, err = client.Preview(t.Context(), "https://merchant.fr/product")
	if err != nil || rendered.calls != 2 || fallback.calls != 0 {
		t.Fatal("browser price outlived its bounded cache age")
	}
}
