package importer_test

import (
	"context"
	"errors"
	"io"
	"net"
	"net/http"
	"net/netip"
	"slices"
	"strings"
	"syscall"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestDownload_WhenMerchantFailsTemporarily_RetriesTwiceWithinSharedBudget(t *testing.T) {
	// Arrange
	client := importer.New()
	lookups := 0
	client.Resolver = resolverFunc(func(ctx context.Context, _, _ string) ([]netip.Addr, error) {
		lookups++
		return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, ctx.Err()
	})
	var delays []time.Duration
	var sharedDeadline time.Time
	client.Wait = func(ctx context.Context, delay time.Duration) error {
		deadline, ok := ctx.Deadline()
		if !ok || time.Until(deadline) > client.Timeout {
			t.Error("retry has no bounded timeout")
		}
		if !sharedDeadline.IsZero() && !sharedDeadline.Equal(deadline) {
			t.Error("retry reset the download deadline")
		}
		sharedDeadline = deadline
		delays = append(delays, delay)
		return ctx.Err()
	}
	attempts := 0
	calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
		attempts++
		status, body := http.StatusServiceUnavailable, "unavailable"
		if attempts == 3 {
			status, body = http.StatusOK, "product"
		}
		return &http.Response{StatusCode: status, Body: io.NopCloser(strings.NewReader(body)), Header: http.Header{"Content-Type": {"text/html"}}}
	})
	// Act
	body, _, _, err := client.Download(t.Context(), "http://merchant.test/product", 100)
	// Assert
	if err != nil || string(body) != "product" || *calls != 3 || lookups != 3 || !slices.Equal(delays, []time.Duration{250 * time.Millisecond, 500 * time.Millisecond}) {
		t.Fatalf("bounded retry differs: %v, calls %d, lookups %d, delays %v", err, *calls, lookups, delays)
	}
}

func TestDownload_WhenStatusIsPermanentOrRetryAfterCannotFit_DoesNotRetry(t *testing.T) {
	for _, test := range []struct {
		name       string
		status     int
		retryAfter string
	}{
		{"bad request", 400, ""}, {"forbidden", 403, ""}, {"not found", 404, ""}, {"rate limited", 429, "1"},
		{"long retry after", 503, "60"}, {"absolute retry after", 503, "Wed, 21 Oct 2099 07:28:00 GMT"}, {"invalid retry after", 503, "invalid"},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			client.Wait = func(context.Context, time.Duration) error { t.Error("unexpected backoff"); return nil }
			calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
				return &http.Response{StatusCode: test.status, Header: http.Header{"Retry-After": {test.retryAfter}}, Body: io.NopCloser(strings.NewReader("error"))}
			})
			// Act
			_, _, _, err := client.Download(t.Context(), "http://merchant.test/product", 100)
			// Assert
			if err == nil || *calls != 1 {
				t.Fatal("permanent response retried")
			}
		})
	}
}

func TestDownload_WhenTransientNetworkFailureRepeats_StopsAfterThreeAttempts(t *testing.T) {
	for _, failure := range []error{io.EOF, io.ErrUnexpectedEOF, syscall.ECONNRESET, syscall.ECONNREFUSED, &net.DNSError{IsTimeout: true}} {
		t.Run(failure.Error(), func(t *testing.T) {
			// Arrange
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			client.Wait = func(ctx context.Context, _ time.Duration) error { return ctx.Err() }
			calls := 0
			client.Dialer = dialerFunc(func(context.Context, string, string) (net.Conn, error) { calls++; return nil, failure })
			// Act
			_, _, _, err := client.Download(t.Context(), "http://merchant.test/product", 100)
			// Assert
			if err == nil || calls != 3 {
				t.Fatalf("transient failure attempts differ: %d", calls)
			}
		})
	}
}

func TestDownload_WhenCancelledDuringBackoff_StopsBeforeNextConnection(t *testing.T) {
	// Arrange
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.Wait = func(ctx context.Context, _ time.Duration) error { cancel(); return ctx.Err() }
	calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
		return &http.Response{StatusCode: 502, Header: http.Header{}, Body: io.NopCloser(strings.NewReader("error"))}
	})
	// Act
	_, _, _, err := client.Download(ctx, "http://merchant.test/product", 100)
	// Assert
	if !errors.Is(err, context.Canceled) || *calls != 1 {
		t.Fatal("cancelled backoff opened another connection")
	}
}

func TestDownload_WhenMerchantProvidesShortRetryAfter_RespectsItsMinimumDelay(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	waits := 0
	client.Wait = func(ctx context.Context, delay time.Duration) error {
		waits++
		if delay != time.Second {
			t.Error("merchant retry-after was ignored")
		}
		return ctx.Err()
	}
	attempts := 0
	pipeMerchant(t, client, func(*http.Request) *http.Response {
		attempts++
		status := 503
		if attempts == 2 {
			status = 200
		}
		return &http.Response{StatusCode: status, Header: http.Header{"Retry-After": {"1"}}, Body: io.NopCloser(strings.NewReader("product"))}
	})
	// Act
	_, _, _, err := client.Download(t.Context(), "http://merchant.test/product", 100)
	// Assert
	if err != nil || attempts != 2 || waits != 1 {
		t.Fatal("short retry-after was not respected")
	}
}
