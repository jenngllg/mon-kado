package importer

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

func TestLocalFirecrawlKeylessFirst_WhenQuotaIsReached_UsesAuthenticatedAccount(t *testing.T) {
	for _, scenario := range []struct {
		name               string
		status             int
		wantAccount, valid bool
	}{
		{"keyless succeeds", 200, false, true},
		{"keyless rate limited", 429, true, true},
		{"keyless timed out", 408, true, true},
		{"keyless server error", 503, true, true},
		{"keyless forbidden", 403, false, false},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			keylessCalls, accountCalls := 0, 0
			anonymous := firecrawlFixture(t, func(_ context.Context, _ *url.URL, _ string, _ io.Reader, headers http.Header) (*http.Response, error) {
				keylessCalls++
				if headers.Get("Authorization") != "" {
					t.Fatal("keyless request included a credential")
				}
				if scenario.status != 200 {
					return relayResponse(scenario.status, "", "application/json"), nil
				}
				return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
			})
			anonymous.key = ""
			account := firecrawlFixture(t, func(_ context.Context, _ *url.URL, _ string, _ io.Reader, headers http.Header) (*http.Response, error) {
				accountCalls++
				if headers.Get("Authorization") != "Bearer test-provider-key" {
					t.Fatal("account credential not supplied")
				}
				return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
			})
			provider := &LocalFirecrawlKeylessFirst{keyless: anonymous, authenticated: account}
			target, _ := url.Parse("https://shop.merchant.fr/item")

			// Act
			result, err := provider.GetHTML(t.Context(), target)

			// Assert
			if keylessCalls != 1 || (accountCalls == 1) != scenario.wantAccount || (err == nil) != scenario.valid || scenario.valid && (result.Name == nil || *result.Name != "Gift") {
				t.Fatalf("keyless calls %d, account calls %d, error %v", keylessCalls, accountCalls, err)
			}
		})
	}
}

func TestPreview_WhenKeylessImagesAreLimited_SpendsAtMostOneAccountAttemptPerPreview(t *testing.T) {
	// Arrange
	keylessCalls, accountCalls := 0, 0
	anonymous := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		keylessCalls++
		return relayResponse(429, "", "application/json"), nil
	})
	anonymous.key = ""
	account := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		accountCalls++
		return firecrawlImageDocument("aW1hZ2U=", "image/png", "", 200), nil
	})
	account.localRolling = true
	provider := &LocalFirecrawlKeylessFirst{keyless: anonymous, authenticated: account, accountImages: true}
	name, amount := "Gift", json.Number("12")
	metadata := Metadata{Name: &name, Price: &amount, Images: []string{"https://merchant.fr/a", "https://merchant.fr/b"}}
	client := New()
	client.Resolver = &previewTestResolver{answers: [][]netip.Addr{{netip.MustParseAddr("1.1.1.1")}}}
	client.Dialer = previewTestUnavailableDialer{}
	client.PageSource = &previewTestPage{metadata: metadata}
	client.FallbackSource = &previewTestPage{metadata: metadata}
	client.FinalImageSource = provider

	// Act
	first, firstErr := client.Preview(t.Context(), "https://merchant.fr/product")
	second, secondErr := client.Preview(t.Context(), "https://merchant.fr/product")

	// Assert
	if firstErr != nil || secondErr != nil || first.Image != nil || second.Image != nil || keylessCalls != 1 || accountCalls != 2 {
		t.Fatalf("per-preview account limit changed: keyless %d, account %d, errors %v %v", keylessCalls, accountCalls, firstErr, secondErr)
	}
}

func TestLocalFirecrawl_WhenAccountRollingIsEnabled_PageAndImageShareBoundedWindow(t *testing.T) {
	// Arrange
	calls := 0
	client := firecrawlFixture(t, func(_ context.Context, _ *url.URL, _ string, body io.Reader, headers http.Header) (*http.Response, error) {
		calls++
		if headers.Get("Authorization") != "Bearer test-provider-key" {
			t.Fatal("account credential was not supplied")
		}
		var request firecrawlRequest
		if json.NewDecoder(body).Decode(&request) != nil {
			t.Fatal("invalid provider request")
		}
		if len(request.Formats) == 1 && request.Formats[0] == "rawBase64" {
			return firecrawlImageDocument("aW1hZ2U=", "image/png", "", 200), nil
		}
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	client.localRolling = true
	target, _ := url.Parse("https://shop.merchant.fr/item")

	// Act
	page, pageErr := client.GetHTML(t.Context(), target)
	image, imageErr := client.GetImage(t.Context(), target)

	// Assert
	if pageErr != nil || imageErr != nil || calls != 2 || page.Name == nil || string(image) != "image" {
		t.Fatalf("account page/image pacing failed: calls %d, page %v, image %v", calls, pageErr, imageErr)
	}
}

func TestLocalFirecrawl_WhenAccountRollingWindowIsFull_RejectsAdditionalStarts(t *testing.T) {
	// Arrange
	calls := 0
	client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		calls++
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	client.localRolling = true
	now := client.now()
	client.now = func() time.Time { return now }
	target, _ := url.Parse("https://shop.merchant.fr/item")

	// Act
	for i := 0; i < keylessWindowLimit; i++ {
		if _, err := client.Get(t.Context(), target); err != nil {
			t.Fatalf("request %d unexpectedly rejected: %v", i+1, err)
		}
	}
	_, limited := client.Get(t.Context(), target)
	now = now.Add(keylessWindow)
	_, resumed := client.Get(t.Context(), target)

	// Assert
	if limited == nil || resumed != nil || calls != keylessWindowLimit+1 {
		t.Fatalf("account window changed: calls %d, limited %v, resumed %v", calls, limited, resumed)
	}
}

func TestLocalFirecrawl_WhenAccountRollingRequestsAreConcurrent_RespectsSharedWindow(t *testing.T) {
	// Arrange
	var calls atomic.Int32
	client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		calls.Add(1)
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	client.localRolling = true
	target, _ := url.Parse("https://shop.merchant.fr/item")
	const workers = 20
	results := make(chan error, workers)
	var group sync.WaitGroup

	// Act
	for range workers {
		group.Add(1)
		go func() {
			defer group.Done()
			_, err := client.Get(t.Context(), target)
			results <- err
		}()
	}
	group.Wait()
	close(results)

	// Assert
	succeeded := 0
	for err := range results {
		if err == nil {
			succeeded++
		}
	}
	if succeeded != keylessWindowLimit || calls.Load() != keylessWindowLimit {
		t.Fatalf("account starts escaped the rolling limit: succeeded %d, calls %d", succeeded, calls.Load())
	}
}

func TestLocalFirecrawl_WhenKeylessRequestsAreConcurrent_RespectsSharedWindow(t *testing.T) {
	// Arrange
	var calls atomic.Int32
	client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		calls.Add(1)
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	client.key = ""
	target, _ := url.Parse("https://shop.merchant.fr/item")
	const workers = 20
	results := make(chan error, workers)
	var group sync.WaitGroup

	// Act
	for i := 0; i < workers; i++ {
		group.Add(1)
		go func() {
			defer group.Done()
			_, err := client.Get(t.Context(), target)
			results <- err
		}()
	}
	group.Wait()
	close(results)

	// Assert
	var succeeded, paced int
	for err := range results {
		switch {
		case err == nil:
			succeeded++
		case errors.Is(err, errKeylessBusy):
			paced++
		default:
			t.Fatalf("unexpected concurrent result: %v", err)
		}
	}
	if succeeded != keylessWindowLimit || paced != workers-keylessWindowLimit || int(calls.Load()) != keylessWindowLimit {
		t.Fatalf("window changed: successes %d, paced %d, calls %d", succeeded, paced, calls.Load())
	}
}

func TestLocalFirecrawlKeylessFirst_WhenQuotaCooldownIsActive_DoesNotRepeatKeylessRequest(t *testing.T) {
	// Arrange
	keylessCalls, accountCalls := 0, 0
	anonymous := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		keylessCalls++
		return relayResponse(429, "", "application/json"), nil
	})
	anonymous.key = ""
	account := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		accountCalls++
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	account.wait = func(context.Context, time.Duration) error { return nil }
	provider := &LocalFirecrawlKeylessFirst{keyless: anonymous, authenticated: account}
	target, _ := url.Parse("https://shop.merchant.fr/item")

	// Act
	_, first := provider.Get(t.Context(), target)
	_, second := provider.Get(t.Context(), target)

	// Assert
	if first != nil || second != nil || keylessCalls != 1 || accountCalls != 2 {
		t.Fatalf("keyless calls %d, account calls %d, errors %v %v", keylessCalls, accountCalls, first, second)
	}
}

func TestLocalFirecrawlKeylessFirst_WhenLongRetryAfterIsReturned_ResumesOnlyAfterCooldown(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 28, 21, 32, 55, 0, time.UTC)
	keylessCalls, accountCalls := 0, 0
	anonymous := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		keylessCalls++
		if keylessCalls == 1 {
			response := relayResponse(429, "", "application/json")
			response.Header.Set("Retry-After", "77997")
			return response, nil
		}
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	anonymous.key = ""
	anonymous.now = func() time.Time { return now }
	account := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		accountCalls++
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	account.now = func() time.Time { return now }
	account.wait = func(context.Context, time.Duration) error { return nil }
	provider := &LocalFirecrawlKeylessFirst{keyless: anonymous, authenticated: account}
	target, _ := url.Parse("https://shop.merchant.fr/item")

	// Act
	_, first := provider.GetHTML(t.Context(), target)
	now = now.Add(77997*time.Second - time.Millisecond)
	_, second := provider.GetHTML(t.Context(), target)
	beforeExpiry := keylessCalls
	now = now.Add(time.Millisecond)
	_, third := provider.GetHTML(t.Context(), target)

	// Assert
	if first != nil || second != nil || third != nil || beforeExpiry != 1 || keylessCalls != 2 || accountCalls != 2 {
		t.Fatalf("cooldown changed: keyless %d before expiry, %d total; account %d; errors %v %v %v", beforeExpiry, keylessCalls, accountCalls, first, second, third)
	}
}

func TestLocalFirecrawl_WhenKeylessRequestsShareWindow_AllowsBurstAndBoundsStarts(t *testing.T) {
	// Arrange
	calls := 0
	client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		calls++
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	client.key = ""
	now := client.now()
	client.now = func() time.Time { return now }
	target, _ := url.Parse("https://shop.merchant.fr/item")

	// Act
	for i := 0; i < keylessWindowLimit; i++ {
		if _, err := client.Get(t.Context(), target); err != nil {
			t.Fatalf("request %d unexpectedly rejected: %v", i+1, err)
		}
	}
	_, limited := client.Get(t.Context(), target)
	now = now.Add(keylessWindow)
	_, resumed := client.Get(t.Context(), target)

	// Assert
	if !errors.Is(limited, errKeylessBusy) || resumed != nil || calls != keylessWindowLimit+1 {
		t.Fatalf("window limit or expiry incorrect: calls %d, limited %v, resumed %v", calls, limited, resumed)
	}
}

func TestLocalFirecrawlKeylessFirst_WhenPageAndImageAreRequested_UsesSameFreeWindow(t *testing.T) {
	// Arrange
	calls := 0
	anonymous := firecrawlFixture(t, func(_ context.Context, _ *url.URL, _ string, body io.Reader, headers http.Header) (*http.Response, error) {
		calls++
		if headers.Get("Authorization") != "" {
			t.Fatal("keyless request included a credential")
		}
		var request firecrawlRequest
		if json.NewDecoder(body).Decode(&request) != nil {
			t.Fatal("invalid provider request")
		}
		if len(request.Formats) == 1 && request.Formats[0] == "rawBase64" {
			return firecrawlImageDocument("aW1hZ2U=", "image/png", "", 200), nil
		}
		return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
	})
	anonymous.key = ""
	account := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		t.Fatal("image recovery spent an account credit")
		return nil, nil
	})
	provider := &LocalFirecrawlKeylessFirst{keyless: anonymous, authenticated: account}
	target, _ := url.Parse("https://shop.merchant.fr/item")

	// Act
	page, pageErr := provider.GetHTML(t.Context(), target)
	image, imageErr := provider.GetImage(t.Context(), target)

	// Assert
	if pageErr != nil || imageErr != nil || calls != 2 || page.Name == nil || string(image) != "image" {
		t.Fatalf("page/image recovery failed: calls %d, page %v, image %v", calls, pageErr, imageErr)
	}
}

func TestLocalFirecrawlKeylessFirst_WhenImageQuotaIsReached_DoesNotSpendAccountCredits(t *testing.T) {
	// Arrange
	keylessCalls, accountCalls := 0, 0
	anonymous := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		keylessCalls++
		return relayResponse(429, "", "application/json"), nil
	})
	anonymous.key = ""
	account := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		accountCalls++
		return nil, nil
	})
	provider := &LocalFirecrawlKeylessFirst{keyless: anonymous, authenticated: account}
	target, _ := url.Parse("https://shop.merchant.fr/image")

	// Act
	_, err := provider.GetImage(t.Context(), target)

	// Assert
	if !errors.Is(err, errKeylessQuota) || keylessCalls != 1 || accountCalls != 0 {
		t.Fatalf("image quota fallback changed: error %v, keyless %d, account %d", err, keylessCalls, accountCalls)
	}
}
