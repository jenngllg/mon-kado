package importer_test

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"image"
	"image/png"
	"net"
	"net/netip"
	"net/url"
	"sync/atomic"
	"testing"
	"testing/synctest"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/importer"
)

type cachedPageSource struct {
	calls atomic.Int32
	get   func(context.Context, *url.URL) (importer.Metadata, error)
}

func (*cachedPageSource) CanHandle(*url.URL) bool { return true }
func (s *cachedPageSource) Get(ctx context.Context, target *url.URL) (importer.Metadata, error) {
	s.calls.Add(1)
	return s.get(ctx, target)
}

type cachedImageSource struct {
	content []byte
	calls   atomic.Int32
}

func (s *cachedImageSource) GetImage(ctx context.Context, _ *url.URL) ([]byte, error) {
	s.calls.Add(1)
	return s.content, ctx.Err()
}

func cachedPreviewFixture(t *testing.T, now func() time.Time) (*importer.Client, *cachedPageSource, *cachedImageSource) {
	t.Helper()
	var picture bytes.Buffer
	if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	base, _ := url.Parse("https://merchant.test/product")
	page := &cachedPageSource{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
		return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Notebook","image":"/photo","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base), ctx.Err()
	}}
	pictureSource := &cachedImageSource{content: picture.Bytes()}
	client := importer.NewWithPreviewCache(now)
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.Dialer = dialerFunc(func(context.Context, string, string) (net.Conn, error) {
		return nil, errors.New("direct image unavailable")
	})
	client.PageSource, client.ImageSource = page, pictureSource
	return client, page, pictureSource
}

func requireCompleteCachedPreview(t *testing.T, result domain.WishImportPreview, err error) {
	t.Helper()
	if err != nil || result.Name == nil || *result.Name != "Notebook" || result.Price == nil || result.Price.String() != "12.3" || result.Image == nil || result.Image.ContentType != "image/webp" || result.Warnings == nil || len(result.Warnings) != 0 {
		t.Fatalf("complete preview was lost: %v", err)
	}
}

func TestPreview_WhenCompleteProductIsRepeated_UsesIsolatedCacheAndRevalidatesDNS(t *testing.T) {
	// Arrange
	client, page, picture := cachedPreviewFixture(t, time.Now)
	var resolutions atomic.Int32
	client.Resolver = resolverFunc(func(ctx context.Context, _, _ string) ([]netip.Addr, error) {
		resolutions.Add(1)
		return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, ctx.Err()
	})
	// Act
	first, err := client.Preview(t.Context(), "https://merchant.test/product?variant=blue#first")
	requireCompleteCachedPreview(t, first, err)
	*first.Name, *first.Price, first.Image.ContentBase64 = "Edited draft", "999", "changed"
	before := resolutions.Load()
	second, err := client.Preview(t.Context(), "https://merchant.test/product?variant=blue#second")
	// Assert
	requireCompleteCachedPreview(t, second, err)
	if page.calls.Load() != 1 || picture.calls.Load() != 1 || resolutions.Load() != before+1 || *second.Url != "https://merchant.test/product?variant=blue#second" || second.Image.ContentBase64 == "changed" {
		t.Fatal("cache reused mutable state, skipped DNS, lost the current URL or repeated a provider")
	}
	third, err := client.Preview(t.Context(), "https://merchant.test/product?variant=red")
	requireCompleteCachedPreview(t, third, err)
	if page.calls.Load() != 2 {
		t.Fatal("different selected query reused another product preview")
	}
}

func TestPreview_WhenPrimaryPageContainsUnstructuredPrice_AvoidsFallback(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	page.get = func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
		markup := `<meta property="og:title" content="Green ceramic mug"><meta property="og:image" content="https://merchant.test/photo"><main><h1>Green ceramic mug</h1><span class="product-price">12,50 €</span></main>`
		return importer.Extract([]byte(markup), target), ctx.Err()
	}
	fallbackCalls := 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		fallbackCalls++
		return importer.Metadata{}, errors.New("unexpected fallback")
	}}

	// Act
	result, err := client.Preview(t.Context(), "https://merchant.test/product")

	// Assert
	if err != nil || result.Name == nil || result.Price == nil || result.Price.String() != "12.5" || result.Image == nil || fallbackCalls != 0 {
		t.Fatalf("primary page did not provide a complete preview without the fallback: %v", err)
	}
}

func TestPreview_WhenOnlyAuthoritySpellingChanges_ReusesCacheWithoutMergingVariants(t *testing.T) {
	for _, scenario := range []struct {
		name      string
		first     string
		second    string
		wantCalls int32
	}{
		{"HTTPS hostname and default port", "https://merchant.test/product?variant=blue", "https://MERCHANT.TEST:443/product?variant=blue#image", 1},
		{"HTTP hostname and default port", "http://merchant.test/product", "http://MERCHANT.TEST:80/product", 1},
		{"different schemes", "http://merchant.test/product", "https://merchant.test/product", 2},
		{"different selected variants", "https://merchant.test/product?variant=blue", "https://MERCHANT.TEST:443/product?variant=red", 2},
		{"different query order", "https://merchant.test/product?a=1&b=2", "https://MERCHANT.TEST:443/product?b=2&a=1", 2},
		{"different encoded paths", "https://merchant.test/product%2Fblue", "https://MERCHANT.TEST:443/product/blue", 2},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			client, page, _ := cachedPreviewFixture(t, time.Now)
			// Act
			first, err := client.Preview(t.Context(), scenario.first)
			requireCompleteCachedPreview(t, first, err)
			second, err := client.Preview(t.Context(), scenario.second)
			// Assert
			requireCompleteCachedPreview(t, second, err)
			if page.calls.Load() != scenario.wantCalls || second.Url == nil || *second.Url != scenario.second {
				t.Fatal("cache confused product identity or changed the caller's URL")
			}
		})
	}
}

func TestPreview_WhenCacheExpires_RefreshesAtTenMinutesWithoutSlidingExpiry(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 28, 12, 0, 0, 0, time.UTC)
	client, page, _ := cachedPreviewFixture(t, func() time.Time { return now })
	// Act
	first, err := client.Preview(t.Context(), "https://merchant.test/product")
	requireCompleteCachedPreview(t, first, err)
	now = now.Add(599 * time.Second)
	second, err := client.Preview(t.Context(), "https://merchant.test/product")
	requireCompleteCachedPreview(t, second, err)
	if page.calls.Load() != 1 {
		t.Fatal("cache expired before its bounded lifetime")
	}
	now = now.Add(time.Second)
	third, err := client.Preview(t.Context(), "https://merchant.test/product")
	// Assert
	requireCompleteCachedPreview(t, third, err)
	if page.calls.Load() != 2 {
		t.Fatal("cache served an expired price or extended expiry on access")
	}
}

func TestPreview_WhenProductIsPartialOrUnavailable_DoesNotCacheIt(t *testing.T) {
	for _, missing := range []string{"name", "price", "image", "page", "ambiguous image", "unsupported currency"} {
		t.Run(missing, func(t *testing.T) {
			// Arrange
			client, page, picture := cachedPreviewFixture(t, time.Now)
			original := page.get
			page.get = func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
				metadata, err := original(ctx, target)
				switch missing {
				case "name":
					metadata.Name = nil
				case "price":
					metadata.Price = nil
				case "image":
					picture.content = []byte("<svg/>")
				case "page":
					return importer.Metadata{}, errors.New("merchant unavailable")
				case "ambiguous image":
					metadata.Price, metadata.PriceAmbiguous = nil, true
					picture.content = []byte("<svg/>")
				case "unsupported currency":
					metadata.Price, metadata.UnsupportedCurrency = nil, true
				}
				return metadata, err
			}
			// Act
			for range 2 {
				result, err := client.Preview(t.Context(), "https://merchant.test/product")
				if err != nil || len(result.Warnings) == 0 {
					t.Fatal("partial preview failure contract changed")
				}
			}
			// Assert
			if page.calls.Load() != 2 {
				t.Fatal("a partial or unavailable product prevented another actual attempt")
			}
		})
	}
}

func TestPreview_WhenPriceIsExplicitlyAmbiguous_CachesOnlyUntilExpiry(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 28, 12, 0, 0, 0, time.UTC)
	client, page, picture := cachedPreviewFixture(t, func() time.Time { return now })
	original := page.get
	page.get = func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
		metadata, err := original(ctx, target)
		metadata.Price = nil
		metadata.PriceAmbiguous = true
		return metadata, err
	}
	assertAmbiguous := func(result domain.WishImportPreview, err error) {
		t.Helper()
		if err != nil || result.Name == nil || result.Image == nil || result.Price != nil || len(result.Warnings) != 1 || result.Warnings[0] != "WISH_IMPORT_PRICE_UNAVAILABLE" {
			t.Fatalf("ambiguous preview changed: %v, warnings %v", err, result.Warnings)
		}
	}

	// Act
	first, err := client.Preview(t.Context(), "https://merchant.test/product")
	assertAmbiguous(first, err)
	*first.Name = "Changed draft"
	first.Warnings[0] = "changed"
	now = now.Add(599 * time.Second)
	second, err := client.Preview(t.Context(), "https://merchant.test/product")
	assertAmbiguous(second, err)

	// Assert
	if *second.Name != "Notebook" || page.calls.Load() != 1 || picture.calls.Load() != 1 {
		t.Fatal("cached ambiguous result was mutated or provider was called again")
	}
	now = now.Add(time.Second)
	third, err := client.Preview(t.Context(), "https://merchant.test/product")
	assertAmbiguous(third, err)
	if page.calls.Load() != 2 || picture.calls.Load() != 2 {
		t.Fatal("ambiguous preview did not expire")
	}
}

func TestPreview_WhenCachedHostRebindsToPrivateAddress_RejectsBeforeCacheLookup(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	result, err := client.Preview(t.Context(), "https://merchant.test/product")
	requireCompleteCachedPreview(t, result, err)
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("127.0.0.1")}}
	// Act
	result, err = client.Preview(t.Context(), "https://merchant.test/product")
	// Assert
	var business *domain.Error
	if !errors.As(err, &business) || result.Name != nil || result.Image != nil || page.calls.Load() != 1 {
		t.Fatal("cached content bypassed current public-address authorization")
	}
}

func TestPreview_WhenIdenticalRequestsOverlap_UsesOneProviderOperation(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client, page, picture := cachedPreviewFixture(t, time.Now)
		original, release := page.get, make(chan struct{})
		page.get = func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
			select {
			case <-ctx.Done():
				return importer.Metadata{}, ctx.Err()
			case <-release:
				return original(ctx, target)
			}
		}
		results := make(chan domain.WishImportPreview, 5)
		// Act
		for range 5 {
			go func() {
				result, err := client.Preview(t.Context(), "https://merchant.test/product")
				requireCompleteCachedPreview(t, result, err)
				results <- result
			}()
		}
		synctest.Wait()
		if page.calls.Load() != 1 {
			t.Fatal("overlapping callers each consumed a provider request")
		}
		close(release)
		synctest.Wait()
		// Assert
		if len(results) != 5 || page.calls.Load() != 1 || picture.calls.Load() != 1 {
			t.Fatal("completed preview was not shared with every waiting caller")
		}
	})
}

func TestPreview_WhenWaitingCallerCancels_DoesNotCancelTheOwner(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client, page, _ := cachedPreviewFixture(t, time.Now)
		original, release := page.get, make(chan struct{})
		page.get = func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
			select {
			case <-ctx.Done():
				return importer.Metadata{}, ctx.Err()
			case <-release:
				return original(ctx, target)
			}
		}
		owner := make(chan error, 1)
		go func() { _, err := client.Preview(t.Context(), "https://merchant.test/product"); owner <- err }()
		synctest.Wait()
		ctx, cancel := context.WithCancel(t.Context())
		waiter := make(chan error, 1)
		go func() { _, err := client.Preview(ctx, "https://merchant.test/product"); waiter <- err }()
		synctest.Wait()
		// Act
		cancel()
		synctest.Wait()
		// Assert
		if !errors.Is(<-waiter, context.Canceled) || page.calls.Load() != 1 || len(owner) != 0 {
			t.Fatal("waiting caller cancellation changed the owner's operation")
		}
		close(release)
		synctest.Wait()
		if err := <-owner; err != nil {
			t.Fatal(err)
		}
	})
}

func TestPreview_WhenOwnerCancels_WaiterCanStartWithItsOwnContext(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client, page, _ := cachedPreviewFixture(t, time.Now)
		original := page.get
		var calls atomic.Int32
		page.get = func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
			if calls.Add(1) == 1 {
				<-ctx.Done()
				return importer.Metadata{}, ctx.Err()
			}
			return original(ctx, target)
		}
		ctx, cancel := context.WithCancel(t.Context())
		owner := make(chan error, 1)
		go func() { _, err := client.Preview(ctx, "https://merchant.test/product"); owner <- err }()
		synctest.Wait()
		waiter := make(chan error, 1)
		go func() {
			result, err := client.Preview(t.Context(), "https://merchant.test/product")
			requireCompleteCachedPreview(t, result, err)
			waiter <- err
		}()
		synctest.Wait()
		// Act
		cancel()
		synctest.Wait()
		// Assert
		if !errors.Is(<-owner, context.Canceled) || <-waiter != nil || calls.Load() != 2 {
			t.Fatal("canceled owner retained the key or substituted the waiter's context")
		}
	})
}

func TestPreview_WhenEntryLimitIsReached_EvictsLeastRecentlyUsedProduct(t *testing.T) {
	// Arrange
	client, page, _ := cachedPreviewFixture(t, time.Now)
	for i := range 128 {
		result, err := client.Preview(t.Context(), fmt.Sprintf("https://merchant.test/product/%d", i))
		requireCompleteCachedPreview(t, result, err)
	}
	// Act
	for _, id := range []int{0, 128, 0} {
		result, err := client.Preview(t.Context(), fmt.Sprintf("https://merchant.test/product/%d", id))
		requireCompleteCachedPreview(t, result, err)
	}
	// Assert
	if page.calls.Load() != 129 {
		t.Fatal("recently used preview was evicted instead of the oldest one")
	}
	result, err := client.Preview(t.Context(), "https://merchant.test/product/1")
	requireCompleteCachedPreview(t, result, err)
	if page.calls.Load() != 130 {
		t.Fatal("entry cap did not evict the oldest preview")
	}
}

func TestNewWithPreviewCache_WhenClockIsMissing_RejectsInvalidComposition(t *testing.T) {
	// Arrange
	defer func() {
		// Assert
		if recover() == nil {
			t.Fatal("missing cache clock accepted")
		}
	}()
	// Act
	importer.NewWithPreviewCache(nil)
}

func TestPreview_WhenCallerCancelsDuringCacheLookup_DoesNotServeCachedContent(t *testing.T) {
	// Arrange
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	cancelDuringLookup := false
	client, page, _ := cachedPreviewFixture(t, func() time.Time {
		if cancelDuringLookup {
			cancel()
		}
		return time.Now()
	})
	first, err := client.Preview(t.Context(), "https://merchant.test/product")
	requireCompleteCachedPreview(t, first, err)
	cancelDuringLookup = true
	// Act
	result, err := client.Preview(ctx, "https://merchant.test/product")
	// Assert
	if !errors.Is(err, context.Canceled) || result.Name != nil || page.calls.Load() != 1 {
		t.Fatal("canceled cache lookup returned content or started a provider")
	}
}

func TestPreview_WhenPendingKeyLimitIsReached_RemainsBoundedWithoutBlockingDistinctProducts(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client, page, _ := cachedPreviewFixture(t, time.Now)
		original, release := page.get, make(chan struct{})
		page.get = func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
			select {
			case <-ctx.Done():
				return importer.Metadata{}, ctx.Err()
			case <-release:
				return original(ctx, target)
			}
		}
		results := make(chan error, 66)
		launch := func(value string) {
			go func() {
				result, err := client.Preview(t.Context(), value)
				requireCompleteCachedPreview(t, result, err)
				results <- err
			}()
		}
		for i := range 64 {
			launch(fmt.Sprintf("https://merchant.test/product/%d", i))
		}
		synctest.Wait()
		// Act
		launch("https://merchant.test/product/extra")
		launch("https://merchant.test/product/extra")
		synctest.Wait()
		if page.calls.Load() != 66 {
			t.Fatal("full key coordination prevented an independent product from starting")
		}
		close(release)
		synctest.Wait()
		// Assert
		if len(results) != 66 {
			t.Fatal("bounded coordination lost a caller")
		}
		result, err := client.Preview(t.Context(), "https://merchant.test/product/extra")
		requireCompleteCachedPreview(t, result, err)
		if page.calls.Load() != 66 {
			t.Fatal("overlapping independent completion corrupted the stored preview")
		}
	})
}

func TestPreview_WhenImageBytesFillCache_EvictsWithinMemoryBudget(t *testing.T) {
	// Encoding cost under race instrumentation must not decide cache eviction.
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client, page, picture := cachedPreviewFixture(t, time.Now)
		canvas := image.NewNRGBA(image.Rect(0, 0, 1400, 1400))
		state := uint32(0x12345678)
		for i := range canvas.Pix {
			state ^= state << 13
			state ^= state >> 17
			state ^= state << 5
			canvas.Pix[i] = byte(state)
		}
		var content bytes.Buffer
		if err := png.Encode(&content, canvas); err != nil {
			t.Fatal(err)
		}
		picture.content = content.Bytes()
		// Act
		for _, id := range []string{"first", "second", "first"} {
			result, err := client.Preview(t.Context(), "https://merchant.test/product/"+id)
			requireCompleteCachedPreview(t, result, err)
			if len(result.Image.ContentBase64) <= 8*1024*1024 {
				t.Fatal("fixture does not require byte-budget eviction")
			}
		}
		// Assert
		if page.calls.Load() != 3 {
			t.Fatal("two large previews were retained beyond the sixteen-MiB cache budget")
		}
	})
}

func TestPreview_WhenDuplicateWaitsForTimedOutOwner_KeepsTheEightSecondServerBudget(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client, page, _ := cachedPreviewFixture(t, time.Now)
		page.get = func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
			<-ctx.Done()
			return importer.Metadata{}, ctx.Err()
		}
		results := make(chan domain.WishImportPreview, 2)
		started := time.Now()
		launch := func() {
			go func() {
				result, err := client.Preview(t.Context(), "https://merchant.test/product")
				if err != nil {
					t.Error("server budget leaked as an unexpected HTTP failure")
				}
				results <- result
			}()
		}
		// Act
		launch()
		synctest.Wait()
		launch()
		for range 2 {
			result := <-results
			// Assert
			if len(result.Warnings) != 4 || result.Name != nil || result.Image != nil {
				t.Fatal("deadline did not preserve the partial-result contract")
			}
		}
		if time.Since(started) != 8*time.Second || page.calls.Load() != 1 {
			t.Fatalf("duplicate wait reset the deadline or started a late provider: elapsed=%v calls=%d", time.Since(started), page.calls.Load())
		}
	})
}
