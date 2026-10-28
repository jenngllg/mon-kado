package importer

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/url"
	"strings"
	"testing"
	"time"
)

func TestCloudflareRenderer_WhenDocumentValid_PreservesProductQueryAndValidatesBothDNSBoundaries(t *testing.T) {
	// Arrange
	target, _ := url.Parse("https://merchant.fr/product?variant=red#private")
	validations, calls := 0, 0
	client := &CloudflareRenderer{origin: "https://monkado-wish-render-local.account.workers.dev", key: "secret",
		validator: localDirectFake{validate: func(ctx context.Context, value string) error {
			validations++
			if validations == 1 && ctx != t.Context() || !strings.HasPrefix(value, "https://merchant.fr/product?variant=red") {
				t.Fatal("validation context or product identity lost")
			}
			return nil
		}}, request: func(ctx context.Context, destination *url.URL, method string, body io.Reader, headers http.Header) (*http.Response, error) {
			calls++
			deadline, ok := ctx.Deadline()
			var input map[string]string
			if !ok || time.Until(deadline) > cloudflareRenderBudget || destination.String() != "https://monkado-wish-render-local.account.workers.dev/render" || method != "POST" || headers.Get("Authorization") != "Bearer secret" || headers.Get("Content-Type") != "application/json" || json.NewDecoder(body).Decode(&input) != nil || len(input) != 1 || input["url"] != "https://merchant.fr/product?variant=red" {
				t.Fatal("provider contract, secret boundary or deadline changed")
			}
			return relayResponse(200, `{"html":"<title>Gift</title><meta property=\"product:price:amount\" content=\"12.30\"><meta property=\"product:price:currency\" content=\"EUR\">","url":"https://merchant.fr/product?variant=red","statusCode":200}`, "application/json; charset=utf-8"), nil
		}}
	// Act
	metadata, err := client.Get(t.Context(), target)
	// Assert
	if err != nil || calls != 1 || validations != 2 || metadata.Name == nil || *metadata.Name != "Gift" || metadata.Price == nil || metadata.Price.String() != "12.3" {
		t.Fatalf("valid rendered metadata lost: %v %+v", err, metadata)
	}
}

func TestCloudflareRenderer_WhenProviderCannotProveBoundedExactDocument_RejectsWithoutRetry(t *testing.T) {
	for _, tc := range []struct {
		name, body, media  string
		status, maximum    int
		requestErr, dnsErr bool
	}{
		{name: "throttled", status: 429}, {name: "wrong media", status: 200, media: "text/html"},
		{name: "invalid JSON", status: 200, media: "application/json", body: "{"},
		{name: "origin challenge", status: 200, media: "application/json", body: `{"html":"challenge","url":"https://merchant.fr/product","statusCode":403}`},
		{name: "changed product", status: 200, media: "application/json", body: `{"html":"<title>Gift</title>","url":"https://merchant.fr/other","statusCode":200}`},
		{name: "oversized HTML", status: 200, media: "application/json", maximum: 1, body: `{"html":"xx","url":"https://merchant.fr/product","statusCode":200}`},
		{name: "oversized envelope", status: 200, media: "application/json", maximum: 1, body: strings.Repeat("x", 32772+base64.StdEncoding.EncodedLen(maximumRenderedImageBytes))},
		{name: "request failed", requestErr: true}, {name: "DNS rejected", dnsErr: true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			calls := 0
			client := &CloudflareRenderer{origin: "https://monkado-wish-render-local.account.workers.dev", maximumBytes: tc.maximum, validator: localDirectFake{validate: func(context.Context, string) error {
				if tc.dnsErr {
					return rejected()
				}
				return nil
			}}, request: func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				if tc.requestErr {
					return nil, errors.New("private provider detail")
				}
				return relayResponse(tc.status, tc.body, tc.media), nil
			}}
			target, _ := url.Parse("https://merchant.fr/product")
			// Act
			_, err := client.Get(t.Context(), target)
			// Assert
			if err == nil || strings.Contains(err.Error(), "private provider detail") || calls > 1 || tc.dnsErr && calls != 0 {
				t.Fatal("unsafe result accepted or request retried")
			}
		})
	}
}

func TestCloudflareRenderer_WhenCancelledOrImageReserveExhausted_DoesNotContactProvider(t *testing.T) {
	for _, cancelled := range []bool{true, false} {
		// Arrange
		ctx, cancel := context.WithTimeout(t.Context(), 500*time.Millisecond)
		if cancelled {
			cancel()
		}
		client := &CloudflareRenderer{origin: "https://monkado-wish-render-local.account.workers.dev", validator: localDirectFake{validate: func(context.Context, string) error { return nil }}, request: func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
			t.Fatal("budget-exhausted provider called")
			return nil, nil
		}}
		target, _ := url.Parse("https://merchant.fr/product")
		// Act
		_, err := client.Get(ctx, target)
		cancel()
		// Assert
		if err == nil {
			t.Fatal("exhausted budget ignored")
		}
	}
}
