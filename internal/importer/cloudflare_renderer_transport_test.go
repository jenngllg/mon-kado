package importer

import (
	"context"
	"errors"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"slices"
	"strings"
	"sync/atomic"
	"testing"
)

func TestCloudflareRenderer_WhenProviderConnects_PinsDedicatedAuthorityAndPreservesTLS(t *testing.T) {
	// Arrange
	client := New()
	client.Resolver = managedResolver(func(context.Context, string, string) ([]netip.Addr, error) {
		return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, nil
	})
	probe := relayProtocolDialer{make(chan relayHello, 1), make(chan struct{}), t.Context(), &atomic.Int32{}}
	client.Dialer = probe
	target, _ := url.Parse("https://merchant.fr/product")
	// Act
	_, err := NewLocalCloudflareRenderer(client, "https://monkado-wish-render-local.account.workers.dev", strings.Repeat("a", 64)).Get(t.Context(), target)
	// Assert
	if err == nil || probe.calls.Load() != 1 {
		t.Fatal("pinned connection missing or retried")
	}
	<-probe.closed
	hello := <-probe.hello
	if hello.serverName != "monkado-wish-render-local.account.workers.dev" || slices.Contains(hello.protocols, "h2") {
		t.Fatal("provider authority or HTTP protocol changed")
	}
	for _, version := range hello.versions {
		if version < 0x0303 {
			t.Fatal("TLS minimum weakened")
		}
	}
}

type renderedBodyFailure struct{}

func (renderedBodyFailure) Read([]byte) (int, error) { return 0, errors.New("private body failure") }
func (renderedBodyFailure) Close() error             { return nil }

func TestCloudflareRenderer_WhenInputTransportOrFinalDNSInvalid_DoesNotExposeProviderDetails(t *testing.T) {
	for _, scenario := range []string{"unsafe input", "invalid origin", "cancelled request", "broken body", "invalid final URL", "credentials in final URL", "final DNS"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			ctx, cancel := context.WithCancel(t.Context())
			defer cancel()
			target, _ := url.Parse("https://merchant.fr/product")
			calls, validations := 0, 0
			client := &CloudflareRenderer{origin: "https://monkado-wish-render-local.account.workers.dev", validator: localDirectFake{validate: func(context.Context, string) error {
				validations++
				if scenario == "final DNS" && validations == 2 {
					return rejected()
				}
				return nil
			}}, request: func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				if scenario == "cancelled request" {
					cancel()
					return nil, errors.New("private transport failure")
				}
				if scenario == "broken body" {
					return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": {"application/json"}}, Body: renderedBodyFailure{}}, nil
				}
				final := "https://merchant.fr/product"
				if scenario == "invalid final URL" {
					final = "%"
				}
				if scenario == "credentials in final URL" {
					final = "https://u:p@merchant.fr/product"
				}
				return relayResponse(200, `{"html":"<title>Gift</title>","url":"`+final+`","statusCode":200}`, "application/json"), nil
			}}
			if scenario == "unsafe input" {
				target, _ = url.Parse("https://127.0.0.1/")
			}
			if scenario == "invalid origin" {
				client.origin = "http://evil.fr"
			}
			// Act
			_, err := client.Get(ctx, target)
			// Assert
			if err == nil || strings.Contains(err.Error(), "private") || calls > 1 {
				t.Fatal("invalid result accepted or provider details leaked")
			}
			if scenario == "cancelled request" && !errors.Is(err, context.Canceled) {
				t.Fatal("caller cancellation lost")
			}
			if (scenario == "unsafe input" || scenario == "invalid origin") && calls != 0 {
				t.Fatal("unsafe provider request")
			}
		})
	}
}
