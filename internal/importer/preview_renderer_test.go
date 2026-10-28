package importer_test

import (
	"bytes"
	"context"
	"errors"
	"image"
	"image/png"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"reflect"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestPreview_WhenBrowserRecoveryAvailable_UsesItBeforeMeteredFallbackAndPreservesPrimaryFields(t *testing.T) {
	for _, scenario := range []string{"complete primary", "rendered recovery", "rendered picture", "failed browser", "unsupported currency"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			var picture bytes.Buffer
			if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
				t.Fatal(err)
			}
			base, _ := url.Parse("http://merchant.test/product?variant=red")
			full := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/photo","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base)
			primary := full
			if scenario != "complete primary" {
				primary.Price, primary.Image, primary.Images = nil, nil, nil
			}
			primary.UnsupportedCurrency = scenario == "unsupported currency"
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			var order []string
			client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
				order = append(order, "primary")
				return primary, nil
			}}
			client.RenderedSource = fallbackPage{get: func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
				order = append(order, "browser")
				if target.String() != base.String() {
					t.Fatal("browser product query lost")
				}
				if _, ok := ctx.Deadline(); !ok {
					t.Fatal("browser deadline lost")
				}
				if scenario == "failed browser" {
					return importer.Metadata{}, errors.New("unavailable")
				}
				if scenario == "rendered picture" {
					full.ImageData = picture.Bytes()
				}
				return full, nil
			}}
			client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
				order = append(order, "metered")
				return importer.Metadata{}, errors.New("unavailable")
			}}
			pipeMerchant(t, client, func(r *http.Request) *http.Response {
				if scenario == "rendered picture" {
					t.Fatal("browser picture was downloaded again")
				}
				if r.URL.Path != "/photo" || r.Header.Get("Authorization") != "" {
					t.Fatal("non-image request or provider credential leaked")
				}
				return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": {"image/png"}}, Body: io.NopCloser(bytes.NewReader(picture.Bytes()))}
			})
			// Act
			preview, err := client.Preview(t.Context(), base.String())
			// Assert
			want := []string{"primary", "browser"}
			if scenario == "complete primary" {
				want = []string{"primary"}
			}
			if scenario == "failed browser" || scenario == "unsupported currency" {
				want = append(want, "metered")
			}
			if err != nil || !reflect.DeepEqual(order, want) || preview.Name == nil || *preview.Name != "Gift" {
				t.Fatalf("provider order/available fields changed: %v %v", err, order)
			}
			if scenario == "complete primary" || scenario == "rendered recovery" || scenario == "rendered picture" {
				if preview.Price == nil || preview.Image == nil || len(preview.Warnings) != 0 {
					t.Fatal("complete preview lost")
				}
			}
			if scenario == "unsupported currency" && preview.Price != nil {
				t.Fatal("browser overwrote unsupported currency")
			}
		})
	}
}
