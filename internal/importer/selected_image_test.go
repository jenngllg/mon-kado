package importer_test

import (
	"context"
	"github.com/JennGllg/mon-kado/internal/importer"
	"io"
	"net/http"
	"net/netip"
	"strings"
	"testing"
)

func TestDownloadSelectedImage_WhenRedirectTargetsPrivateAddress_RejectsWithoutConnecting(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFunc(func(ctx context.Context, _, host string) ([]netip.Addr, error) {
		if host == "private.test" {
			return []netip.Addr{netip.MustParseAddr("127.0.0.1")}, ctx.Err()
		}
		return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, ctx.Err()
	})
	calls := pipeMerchant(t, client, func(req *http.Request) *http.Response {
		if req.Header.Get("Accept") != "image/jpeg,image/png,image/webp" {
			t.Errorf("unexpected negotiation")
		}
		return &http.Response{StatusCode: 302, Header: http.Header{"Location": []string{"http://private.test/image"}}, Body: io.NopCloser(strings.NewReader(""))}
	})
	// Act
	_, _, _, err := client.DownloadSelectedImage(t.Context(), "http://merchant.test/image", 1024)
	// Assert
	if err == nil || *calls != 1 {
		t.Fatalf("private redirect was accepted or retried: %v, calls %d", err, *calls)
	}
}
