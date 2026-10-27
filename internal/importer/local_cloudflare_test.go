package importer

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/url"
	"strings"
	"testing"
	"time"
)

type localRelayTransport func(*http.Request) (*http.Response, error)

func (f localRelayTransport) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }

type localDirectFake struct {
	validate func(context.Context, string) error
	download func(context.Context, string, int64) ([]byte, *url.URL, string, error)
}

func (f localDirectFake) Validate(ctx context.Context, address string) error {
	return f.validate(ctx, address)
}
func (f localDirectFake) Download(ctx context.Context, address string, maximum int64) ([]byte, *url.URL, string, error) {
	return f.download(ctx, address, maximum)
}

func relayResponse(status int, body, media string) *http.Response {
	return &http.Response{StatusCode: status, Header: http.Header{"Content-Type": {media}}, Body: io.NopCloser(strings.NewReader(body))}
}

func TestLocalCloudflare_WhenAuthorityNotApproved_DoesNotUseRelay(t *testing.T) {
	for _, address := range []string{"https://www.fnac.com.evil.test/", "https://www.fnac.com./", "ftp://www.fnac.com/", "https://user@www.fnac.com/", "https://www.fnac.com:8443/", "http://www.fnac.com:443/", "https://127.0.0.1/", "https://[::1]/", "https://metadata.google.internal/", "https://shop.home.arpa/"} {
		t.Run(address, func(t *testing.T) {
			// Arrange
			target, _ := url.Parse(address)
			client := NewLocalCloudflare(New(), "test")
			// Act
			accepted := client.CanHandle(target)
			_, err := client.Get(t.Context(), target)
			// Assert
			if accepted || err == nil {
				t.Fatal("unapproved authority accepted")
			}
		})
	}
}

func TestLocalCloudflare_WhenAnyPublicMerchantUsed_TriesCloudflareBeforeDirectEveryTime(t *testing.T) {
	for _, address := range []string{
		"https://www.new-merchant.fr/product", "https://shop.independent-store.com/product",
		"https://www.cultura.com/product", "https://www.boulanger.com/product", "https://www.leroymerlin.fr/product", "https://www.maisonsdumonde.com/product",
		"http://www.new-merchant.fr:80/product#private", "https://WWW.NEW-MERCHANT.FR:443/product",
	} {
		t.Run(address, func(t *testing.T) {
			// Arrange
			target, _ := url.Parse(address)
			validations, edgeCalls := 0, 0
			client := &LocalCloudflare{validator: localDirectFake{validate: func(ctx context.Context, value string) error {
				validations++
				if ctx != t.Context() || value != address {
					t.Fatal("validation context or URL changed")
				}
				return nil
			}, download: func(context.Context, string, int64) ([]byte, *url.URL, string, error) {
				t.Fatal("direct used before Cloudflare")
				return nil, nil, "", nil
			}}, http: &http.Client{Transport: localRelayTransport(func(r *http.Request) (*http.Response, error) {
				edgeCalls++
				var input map[string]string
				if json.NewDecoder(r.Body).Decode(&input) != nil || len(input) != 1 {
					t.Fatal("relay request contract changed")
				}
				forwarded, err := url.Parse(input["url"])
				if err != nil || forwarded.Scheme != "https" || forwarded.Port() != "" || forwarded.Fragment != "" || forwarded.Hostname() != strings.ToLower(target.Hostname()) {
					t.Fatal("relay authority or HTTPS upgrade changed")
				}
				return relayResponse(200, `<title>New merchant product</title><meta property="og:image" content="/cover.jpg">`, "text/html"), nil
			})}}
			// Act / Assert: repeat to ensure the first-ever import already uses Cloudflare.
			for range 2 {
				metadata, err := client.Get(t.Context(), target)
				if err != nil || metadata.Name == nil || *metadata.Name != "New merchant product" || metadata.Image == nil || *metadata.Image != "https://"+strings.ToLower(target.Hostname())+"/cover.jpg" {
					t.Fatal("Cloudflare result or relative image base lost")
				}
			}
			if edgeCalls != 2 || validations != 2 {
				t.Fatal("provider order or DNS revalidation changed")
			}
		})
	}
}

func TestLocalCloudflare_WhenDNSRejected_DoesNotContactRelayOrDirect(t *testing.T) {
	// Arrange
	target, _ := url.Parse("https://www.placedeslibraires.fr/product")
	client := &LocalCloudflare{validator: localDirectFake{validate: func(context.Context, string) error { return rejected() }, download: func(context.Context, string, int64) ([]byte, *url.URL, string, error) {
		t.Fatal("direct used after rejected DNS")
		return nil, nil, "", nil
	}}, http: &http.Client{Transport: localRelayTransport(func(*http.Request) (*http.Response, error) { t.Fatal("relay used after rejected DNS"); return nil, nil })}}
	// Act
	_, err := client.Get(t.Context(), target)
	// Assert
	if err == nil {
		t.Fatal("rejected DNS accepted")
	}
}

func TestLocalCloudflare_WhenMetadataAvailable_AuthenticatesAndSkipsDirect(t *testing.T) {
	// Arrange
	target, _ := url.Parse("https://www.placedeslibraires.fr/product#private")
	calls := 0
	client := &LocalCloudflare{key: "test-key", validator: localDirectFake{validate: func(context.Context, string) error { return nil }, download: func(context.Context, string, int64) ([]byte, *url.URL, string, error) {
		t.Fatal("redundant direct download")
		return nil, nil, "", nil
	}}, http: &http.Client{Transport: localRelayTransport(func(r *http.Request) (*http.Response, error) {
		calls++
		body, _ := io.ReadAll(r.Body)
		deadline, ok := r.Context().Deadline()
		if r.URL.String() != localCloudflareEndpoint || r.Method != "POST" || r.Header.Get("Authorization") != "Bearer test-key" || !ok || time.Until(deadline) > localCloudflareBudget || strings.Contains(string(body), "private") {
			t.Fatal("relay authority, authentication or budget changed")
		}
		return relayResponse(200, "<title>Cloudflare product</title>", "text/html; charset=utf-8"), nil
	})}}
	// Act
	metadata, err := client.Get(t.Context(), target)
	// Assert
	if err != nil || calls != 1 || metadata.Name == nil || *metadata.Name != "Cloudflare product" {
		t.Fatal("primary relay metadata lost")
	}
}

func TestLocalCloudflare_WhenRelayFailsOrMetadataIsEmpty_DoesNotDownloadMerchantPage(t *testing.T) {
	for _, scenario := range []struct {
		name, body, media string
		status, maximum   int
		failure           error
	}{
		{name: "blocked", status: 502},
		{name: "redirect", status: 302},
		{name: "transport unavailable", failure: errors.New("unavailable")},
		{name: "deadline exceeded", failure: context.DeadlineExceeded},
		{name: "empty metadata", status: 200, body: "<html></html>", media: "text/html"},
		{name: "wrong media", status: 200, body: "{}", media: "application/json"},
		{name: "too large", status: 200, body: strings.Repeat("x", 65), media: "text/html", maximum: 64},
		{name: "invalid charset", status: 200, body: "<title>Product</title>", media: "text/html; charset=unknown"},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			target, _ := url.Parse("https://www.placedeslibraires.fr/product")
			calls, directCalls := 0, 0
			ctx := t.Context()
			client := &LocalCloudflare{wait: func(context.Context, time.Duration) error { return nil }, maximumBytes: scenario.maximum, validator: localDirectFake{validate: func(received context.Context, address string) error {
				if received != ctx {
					t.Fatal("validation context lost")
				}
				return nil
			}, download: func(received context.Context, address string, maximum int64) ([]byte, *url.URL, string, error) {
				directCalls++
				if received != ctx || received.Err() != nil || address != target.String() || maximum <= 0 {
					t.Fatal("fallback context or limit changed")
				}
				return []byte("<title>Direct product</title>"), target, "text/html", nil
			}}, http: &http.Client{Transport: localRelayTransport(func(*http.Request) (*http.Response, error) {
				calls++
				if scenario.failure != nil {
					return nil, scenario.failure
				}
				return relayResponse(scenario.status, scenario.body, scenario.media), nil
			})}}
			// Act
			metadata, err := client.Get(ctx, target)
			// Assert
			wantCalls := 1
			if scenario.status == 502 || errors.Is(scenario.failure, context.DeadlineExceeded) {
				wantCalls = 2
			}
			if (err == nil) != (scenario.name == "empty metadata") || calls != wantCalls || directCalls != 0 || metadata.Name != nil {
				t.Fatal("relay failure started a direct page fallback or lost its result")
			}
		})
	}
}

func TestLocalCloudflare_WhenRelayFailureDiffers_RetriesOnlyTemporaryFailuresWithoutDirect(t *testing.T) {
	for _, scenario := range []struct {
		name               string
		status             int
		origin, retryAfter string
		wantCalls          int
	}{
		{"relay temporarily unavailable", 503, "", "", 2},
		{"merchant temporary failure", 502, "503", "", 2},
		{"merchant forbidden", 502, "403", "", 1},
		{"merchant rate limit", 502, "429", "", 1},
		{"merchant redirect", 502, "302", "", 1},
		{"merchant asks to wait", 502, "503", "60", 1},
		{"merchant asks for later date", 502, "503", "later", 1},
		{"relay rejects authentication", 401, "", "", 1},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			target, _ := url.Parse("https://www.new-merchant.fr/product")
			calls, direct, waits := 0, 0, 0
			client := &LocalCloudflare{wait: func(ctx context.Context, delay time.Duration) error {
				waits++
				if delay != firstRetryDelay || ctx.Err() != nil {
					t.Fatal("retry budget lost")
				}
				return nil
			}, validator: localDirectFake{validate: func(context.Context, string) error { return nil }, download: func(context.Context, string, int64) ([]byte, *url.URL, string, error) {
				direct++
				if calls != scenario.wantCalls {
					t.Fatal("direct used before edge attempts ended")
				}
				return []byte("<title>Direct</title>"), target, "text/html", nil
			}}, http: &http.Client{Transport: localRelayTransport(func(*http.Request) (*http.Response, error) {
				calls++
				r := relayResponse(scenario.status, "", "")
				r.Header.Set("X-MonKado-Origin-Status", scenario.origin)
				r.Header.Set("X-MonKado-Origin-Retry-After", scenario.retryAfter)
				return r, nil
			})}}
			// Act
			metadata, err := client.Get(t.Context(), target)
			// Assert
			if err == nil || metadata.Name != nil || direct != 0 || calls != scenario.wantCalls || waits != scenario.wantCalls-1 {
				t.Fatal("retry classification or direct fallback changed")
			}
		})
	}
}

func TestLocalCloudflare_WhenSecondAttemptSucceeds_DoesNotUseDirect(t *testing.T) {
	// Arrange
	target, _ := url.Parse("https://www.new-merchant.fr/product")
	calls := 0
	client := &LocalCloudflare{wait: func(context.Context, time.Duration) error { return nil }, validator: localDirectFake{validate: func(context.Context, string) error { return nil }, download: func(context.Context, string, int64) ([]byte, *url.URL, string, error) {
		t.Fatal("direct used after edge success")
		return nil, nil, "", nil
	}}, http: &http.Client{Transport: localRelayTransport(func(*http.Request) (*http.Response, error) {
		calls++
		if calls == 1 {
			return relayResponse(503, "", ""), nil
		}
		return relayResponse(200, "<title>Recovered</title>", "text/html"), nil
	})}}
	// Act
	metadata, err := client.Get(t.Context(), target)
	// Assert
	if err != nil || calls != 2 || metadata.Name == nil || *metadata.Name != "Recovered" {
		t.Fatal("temporary recovery failed")
	}
}

func TestLocalCloudflare_WhenCallerCancelsDuringRetryWait_StopsBeforeRetryOrDirect(t *testing.T) {
	// Arrange
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	target, _ := url.Parse("https://www.new-merchant.fr/product")
	calls := 0
	client := &LocalCloudflare{wait: func(waitContext context.Context, delay time.Duration) error { cancel(); return waitContext.Err() }, validator: localDirectFake{validate: func(context.Context, string) error { return nil }, download: func(context.Context, string, int64) ([]byte, *url.URL, string, error) {
		t.Fatal("direct used after cancellation")
		return nil, nil, "", nil
	}}, http: &http.Client{Transport: localRelayTransport(func(*http.Request) (*http.Response, error) { calls++; return relayResponse(503, "", ""), nil })}}
	// Act
	_, err := client.Get(ctx, target)
	// Assert
	if !errors.Is(err, context.Canceled) || calls != 1 {
		t.Fatal("cancellation did not stop retry")
	}
}

func TestLocalCloudflare_WhenCallerCancels_DoesNotStartFallback(t *testing.T) {
	// Arrange
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	target, _ := url.Parse("https://www.placedeslibraires.fr/product")
	client := &LocalCloudflare{validator: localDirectFake{validate: func(context.Context, string) error { return nil }, download: func(context.Context, string, int64) ([]byte, *url.URL, string, error) {
		t.Fatal("fallback after cancellation")
		return nil, nil, "", nil
	}}, http: &http.Client{Transport: localRelayTransport(func(r *http.Request) (*http.Response, error) { cancel(); return nil, r.Context().Err() })}}
	// Act
	_, err := client.Get(ctx, target)
	// Assert
	if !errors.Is(err, context.Canceled) {
		t.Fatal("caller cancellation lost")
	}
}
