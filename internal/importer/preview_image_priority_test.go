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
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

type priorityImageSource struct {
	content []byte
	order   *[]string
}

func TestPreview_WhenFreeImageSourceSucceeds_DoesNotUseAccountImageFallback(t *testing.T) {
	// Arrange
	client, _, picture := cachedPreviewFixture(t, time.Now)
	var calls []string
	client.FinalImageSource = importer.NewLocalSingleImageAttempt(priorityImageSource{order: &calls})
	client.FallbackSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
		return importer.Metadata{}, ctx.Err()
	}}

	// Act
	result, err := client.Preview(t.Context(), "https://merchant.test/product")

	// Assert
	requireCompleteCachedPreview(t, result, err)
	if picture.calls.Load() != 1 || len(calls) != 0 {
		t.Fatal("a successful free image must not spend an account image request")
	}
}

func (s priorityImageSource) GetImage(ctx context.Context, _ *url.URL) ([]byte, error) {
	*s.order = append(*s.order, "image")
	return s.content, ctx.Err()
}

func TestPreview_WhenOnlyImageMissing_PrioritizesBytesWithoutRenderingKnownText(t *testing.T) {
	for _, missing := range []string{"image", "price and image", "name and image"} {
		t.Run(missing, func(t *testing.T) {
			// Arrange
			var picture bytes.Buffer
			if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
				t.Fatal(err)
			}
			base, _ := url.Parse("http://merchant.test/product")
			complete := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Notebook","image":"/photo","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base)
			primary := complete
			if missing == "price and image" {
				primary.Price = nil
			}
			if missing == "name and image" {
				primary.Name = nil
			}
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			client.PageSource = metadataSource{metadata: primary}
			var order []string
			client.FallbackSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
				order = append(order, "page")
				return complete, ctx.Err()
			}}
			client.FinalImageSource = priorityImageSource{content: picture.Bytes(), order: &order}
			calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
				return &http.Response{StatusCode: 403, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(""))}
			})
			// Act
			result, err := client.Preview(t.Context(), base.String())
			// Assert
			wantOrder := []string{"image"}
			if missing != "image" {
				wantOrder = []string{"page", "image"}
			}
			if err != nil || result.Name == nil || *result.Name != "Notebook" || result.Price == nil || result.Price.String() != "12.3" || result.Image == nil || result.Image.ContentType != "image/webp" || *calls != 1 || !reflect.DeepEqual(order, wantOrder) {
				t.Fatalf("missing resource priority changed: %v %v", err, order)
			}
		})
	}
}

func TestPreview_WhenPrioritizedImageIsInvalid_CanFindAlternativeWithoutOverwritingKnownText(t *testing.T) {
	// Arrange
	var picture bytes.Buffer
	if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	base, _ := url.Parse("http://merchant.test/product")
	primary := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Notebook","image":"/photo","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base)
	alternative := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Alternative title","image":"/available-photo","offers":{"price":"99.00","priceCurrency":"EUR"}}</script>`), base)
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.PageSource = metadataSource{metadata: primary}
	var order []string
	client.FinalImageSource = priorityImageSource{content: []byte("<svg/>"), order: &order}
	client.FallbackSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
		order = append(order, "page")
		return alternative, ctx.Err()
	}}
	calls := pipeMerchant(t, client, func(request *http.Request) *http.Response {
		if request.URL.Path == "/available-photo" {
			return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": {"image/png"}}, Body: io.NopCloser(bytes.NewReader(picture.Bytes()))}
		}
		return &http.Response{StatusCode: 403, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(""))}
	})
	// Act
	result, err := client.Preview(t.Context(), base.String())
	// Assert
	if err != nil || result.Image == nil || result.Name == nil || *result.Name != "Notebook" || result.Price == nil || result.Price.String() != "12.3" || *calls != 2 || !reflect.DeepEqual(order, []string{"image", "page"}) {
		t.Fatal("invalid prioritized image blocked fallback, repeated an attempt or replaced known text")
	}
}
