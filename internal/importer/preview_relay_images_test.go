package importer_test

import (
	"bytes"
	"context"
	"image"
	"image/png"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

type relayImageFake struct {
	calls   int
	content []byte
}

func TestPreview_WhenImageFailedBeforeProductFallback_UsesManagedBytesWithoutRepeatingDirect(t *testing.T) {
	// Arrange
	var picture bytes.Buffer
	if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	metadata := importer.Metadata{Images: []string{"http://merchant.test/photo"}}
	client.PageSource = metadataSource{metadata: metadata}
	client.FallbackSource = metadataSource{metadata: metadata}
	final := &relayImageFake{content: picture.Bytes()}
	client.FinalImageSource = final
	calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
		return &http.Response{StatusCode: 403, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(""))}
	})
	// Act
	preview, err := client.Preview(t.Context(), "http://merchant.test/product")
	// Assert
	if err != nil || preview.Image == nil || final.calls != 1 || *calls != 1 {
		t.Fatalf("managed fallback lost or direct repeated %v %d %d", err, final.calls, *calls)
	}
}

type imageAddressResolver struct{}

func (imageAddressResolver) LookupNetIP(ctx context.Context, network, host string) ([]netip.Addr, error) {
	if host == "merchant.test" {
		return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, ctx.Err()
	}
	return []netip.Addr{netip.MustParseAddr("127.0.0.1")}, ctx.Err()
}

func (f *relayImageFake) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	f.calls++
	return f.content, ctx.Err()
}

func TestPreview_WhenDirectImageFails_UsesRelayBytesThroughExistingNormalizer(t *testing.T) {
	for _, scenario := range []struct {
		name                        string
		directStatus                int
		directContent, relayContent string
		wantImage                   bool
		wantRelayCalls              int
	}{
		{"blocked direct", 403, "", "png", true, 1},
		{"already valid direct", 200, "png", "", true, 0},
		{"unsupported direct", 200, "<svg/>", "png", true, 1},
		{"relay active content rejected", 403, "", "<svg/>", false, 1},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			var imageBytes bytes.Buffer
			if err := png.Encode(&imageBytes, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
				t.Fatal(err)
			}
			content := func(value string) []byte {
				if value == "png" {
					return imageBytes.Bytes()
				}
				return []byte(value)
			}
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			client.PageSource = metadataSource{metadata: importer.Metadata{Images: []string{"http://merchant.test/photo"}}}
			relay := &relayImageFake{content: content(scenario.relayContent)}
			client.ImageSource = relay
			calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
				return &http.Response{StatusCode: scenario.directStatus, Header: http.Header{"Content-Type": []string{"image/png"}}, Body: io.NopCloser(bytes.NewReader(content(scenario.directContent)))}
			})
			// Act
			preview, err := client.Preview(t.Context(), "http://merchant.test/product")
			// Assert
			if err != nil || (preview.Image != nil) != scenario.wantImage || relay.calls != scenario.wantRelayCalls || *calls != 1 {
				t.Fatalf("relay result differs %v %d %d", err, relay.calls, *calls)
			}
			if preview.Image != nil && preview.Image.ContentType != "image/webp" {
				t.Fatal("relay bypassed image normalization")
			}
		})
	}
}

func TestPreview_WhenImageURLIsPrivate_DoesNotForwardItToRelay(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = imageAddressResolver{}
	client.PageSource = metadataSource{metadata: importer.Metadata{Images: []string{"http://127.0.0.1/private"}}}
	relay := &relayImageFake{content: []byte("private")}
	client.ImageSource = relay
	calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
		t.Fatal("private image reached network")
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(""))}
	})
	// Act
	preview, err := client.Preview(t.Context(), "http://merchant.test/product")
	// Assert
	if err != nil || preview.Image != nil || relay.calls != 0 || *calls != 0 {
		t.Fatal("private image leaked to relay")
	}
}
