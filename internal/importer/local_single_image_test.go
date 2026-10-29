package importer

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"testing"
)

func TestPreview_WhenAccountImageFallbackFails_AttemptsOnlyOncePerPreview(t *testing.T) {
	// Arrange
	accountCalls := 0
	account := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		accountCalls++
		return firecrawlImageDocument("aW1hZ2U=", "image/png", "", 200), nil
	})
	account.localRolling = true
	name, amount := "Gift", json.Number("12")
	metadata := Metadata{Name: &name, Price: &amount, Images: []string{"https://merchant.fr/a", "https://merchant.fr/b"}}
	client := New()
	client.Resolver = &previewTestResolver{answers: [][]netip.Addr{{netip.MustParseAddr("1.1.1.1")}}}
	client.Dialer = previewTestUnavailableDialer{}
	client.PageSource = &previewTestPage{metadata: metadata}
	client.FallbackSource = &previewTestPage{metadata: metadata}
	client.FinalImageSource = NewLocalSingleImageAttempt(account)

	// Act
	first, firstErr := client.Preview(t.Context(), "https://merchant.fr/product")
	second, secondErr := client.Preview(t.Context(), "https://merchant.fr/product")

	// Assert
	if firstErr != nil || secondErr != nil || first.Image != nil || second.Image != nil || accountCalls != 2 {
		t.Fatalf("account image attempts must be limited per preview: calls %d, errors %v %v", accountCalls, firstErr, secondErr)
	}
}
