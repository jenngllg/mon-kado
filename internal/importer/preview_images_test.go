package importer_test

import (
	"bytes"
	"context"
	"encoding/base64"
	"github.com/JennGllg/mon-kado/internal/importer"
	"image"
	"image/png"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"os"
	"strings"
	"testing"
)

func TestPreview_WhenMerchantImageIsAVIF_ReturnsNormalizedWebPWithoutChangingTheContract(t *testing.T) {
	// Arrange
	picture, err := os.ReadFile("../images/testdata/avif/alpha-420.avif")
	if err != nil {
		t.Fatal(err)
	}
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.PageSource = metadataSource{metadata: importer.Metadata{Images: []string{"http://merchant.test/photo"}}}
	calls := pipeMerchant(t, client, func(r *http.Request) *http.Response {
		if !strings.Contains(r.Header.Get("Accept"), "image/avif") {
			t.Error("AVIF was not advertised")
		}
		return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": []string{"image/avif"}}, Body: io.NopCloser(bytes.NewReader(picture))}
	})
	// Act
	preview, err := client.Preview(t.Context(), "http://merchant.test/product")
	// Assert
	if err != nil || preview.Image == nil || *calls != 1 || preview.Image.ContentType != "image/webp" {
		t.Fatalf("AVIF preview did not preserve the output contract: %v", err)
	}
	content, err := base64.StdEncoding.DecodeString(preview.Image.ContentBase64)
	if err != nil {
		t.Fatal(err)
	}
	_, format, err := image.DecodeConfig(bytes.NewReader(content))
	if err != nil || format != "webp" {
		t.Fatal("preview image is not a valid WebP")
	}
}

type metadataSource struct{ metadata importer.Metadata }

func (s metadataSource) CanHandle(*url.URL) bool { return true }
func (s metadataSource) Get(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
	return s.metadata, ctx.Err()
}

func TestPreview_WhenFirstProductImageIsBlocked_UsesBoundedAlternateWithoutRefetchingPage(t *testing.T) {
	// Arrange
	var picture bytes.Buffer
	if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.PageSource = metadataSource{metadata: importer.Metadata{Images: []string{"http://merchant.test/blocked", "http://merchant.test/photo", "http://merchant.test/not-needed"}}}
	var paths []string
	calls := pipeMerchant(t, client, func(r *http.Request) *http.Response {
		if r.Header.Get("Accept") != "image/jpeg,image/png,image/webp,image/avif" {
			t.Error("image request did not negotiate only supported formats")
		}
		paths = append(paths, r.URL.Path)
		if r.URL.Path == "/blocked" {
			return &http.Response{StatusCode: 403, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(""))}
		}
		return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": []string{"image/png"}}, Body: io.NopCloser(bytes.NewReader(picture.Bytes()))}
	})
	// Act
	preview, err := client.Preview(t.Context(), "http://merchant.test/product")
	// Assert
	if err != nil || preview.Image == nil || *calls != 2 || strings.Join(paths, ",") != "/blocked,/photo" {
		t.Fatal("image fallback refetched a page, lost normalization or fetched redundant alternatives")
	}
}

func TestPreview_WhenAllAlternateImagesFail_BoundsNetworkAttempts(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.PageSource = metadataSource{metadata: importer.Metadata{Images: []string{"http://merchant.test/1", "http://merchant.test/2", "http://merchant.test/3", "http://merchant.test/4"}}}
	calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
		return &http.Response{StatusCode: 403, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(""))}
	})
	// Act
	preview, err := client.Preview(t.Context(), "http://merchant.test/product")
	// Assert
	if err != nil || preview.Image != nil || *calls != 3 {
		t.Fatal("alternate image requests are not bounded")
	}
}
