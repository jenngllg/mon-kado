package importer_test

import (
	"io"
	"net/http"
	"net/netip"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestDownload_WhenMerchantRedirects_PreservesHonestTransportAndApplicationIdentity(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	calls := pipeMerchant(t, client, func(req *http.Request) *http.Response {
		if req.UserAgent() != "Go-http-client/1.1 MonKado/1.0 (+https://www.monkado.fr)" {
			t.Error("merchant request must identify the actual transport and application")
		}
		if req.Host == "merchant.test" {
			return &http.Response{StatusCode: http.StatusFound, Header: http.Header{"Location": []string{"http://images.test/product.png"}}, Body: io.NopCloser(strings.NewReader(""))}
		}
		return &http.Response{StatusCode: http.StatusOK, Header: http.Header{"Content-Type": []string{"image/png"}}, Body: io.NopCloser(strings.NewReader("image"))}
	})
	// Act
	content, final, media, err := client.Download(t.Context(), "http://merchant.test/product", 100)
	// Assert
	if err != nil || string(content) != "image" || final.Host != "images.test" || media != "image/png" || *calls != 2 {
		t.Fatalf("merchant identity change affected validated downloads: %v", err)
	}
}
