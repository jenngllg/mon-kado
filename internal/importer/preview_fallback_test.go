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
	"slices"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

type fallbackPage struct {
	get func(context.Context, *url.URL) (importer.Metadata, error)
}

func (s fallbackPage) CanHandle(*url.URL) bool { return true }
func (s fallbackPage) Get(ctx context.Context, u *url.URL) (importer.Metadata, error) {
	return s.get(ctx, u)
}

func TestPreview_WhenProvidersReturnEmptyDocuments_ReportsPageUnavailable(t *testing.T) {
	for _, test := range []struct {
		name                      string
		primaryName, fallbackName *string
		wantUnavailable           bool
	}{
		{name: "both empty", wantUnavailable: true},
		{name: "primary name survives empty fallback", primaryName: stringPointer("Primary gift")},
		{name: "fallback name survives empty primary", fallbackName: stringPointer("Fallback gift")},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
				return importer.Metadata{Name: test.primaryName}, nil
			}}
			calls := 0
			client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
				calls++
				return importer.Metadata{Name: test.fallbackName}, nil
			}}
			// Act
			result, err := client.Preview(t.Context(), "https://merchant.test/product")
			// Assert
			if err != nil || calls != 1 || slices.Contains(result.Warnings, "WISH_IMPORT_PAGE_UNAVAILABLE") != test.wantUnavailable {
				t.Fatalf("unexpected page status: calls %d, warnings %v, error %v", calls, result.Warnings, err)
			}
		})
	}
}

func stringPointer(value string) *string { return &value }

func TestPreview_WhenPrimaryFailedOrPartial_UsesFinalProviderOnceAndPreservesAvailableFields(t *testing.T) {
	for _, scenario := range []string{"blocked page", "missing price", "blocked image", "fallback unavailable", "complete primary"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			var picture bytes.Buffer
			if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
				t.Fatal(err)
			}
			base, _ := url.Parse("http://merchant.test/product")
			primary := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Primary gift","image":"/blocked","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base)
			alternative := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Alternative gift","image":"/photo","offers":{"price":"15.50","priceCurrency":"EUR"}}</script>`), base)
			primaryError := error(nil)
			switch scenario {
			case "blocked page":
				primary = importer.Metadata{}
				primaryError = errors.New("unavailable")
			case "missing price":
				primary.Price = nil
				primary.Image = alternative.Image
				primary.Images = alternative.Images
			case "complete primary":
				primary.Image = alternative.Image
				primary.Images = alternative.Images
			}
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			var order []string
			client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
				order = append(order, "primary")
				return primary, primaryError
			}}
			fallbackCalls := 0
			client.FallbackSource = fallbackPage{get: func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
				fallbackCalls++
				order = append(order, "fallback")
				if target.String() != base.String() {
					t.Fatal("fallback URL changed")
				}
				if _, ok := ctx.Deadline(); !ok {
					t.Fatal("shared budget lost")
				}
				if scenario == "fallback unavailable" {
					return importer.Metadata{}, errors.New("unavailable")
				}
				return alternative, nil
			}}
			var paths []string
			pipeMerchant(t, client, func(r *http.Request) *http.Response {
				paths = append(paths, r.URL.Path)
				if r.Header.Get("Authorization") != "" {
					t.Error("provider credential leaked to image")
				}
				if r.URL.Path == "/blocked" {
					return &http.Response{StatusCode: 403, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(""))}
				}
				return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": {"image/png"}}, Body: io.NopCloser(bytes.NewReader(picture.Bytes()))}
			})
			// Act
			preview, err := client.Preview(t.Context(), base.String())
			// Assert
			if err != nil || len(order) == 0 || order[0] != "primary" {
				t.Fatal("provider order changed")
			}
			if scenario == "complete primary" {
				if fallbackCalls != 0 {
					t.Fatal("redundant final provider call")
				}
				return
			}
			if fallbackCalls != 1 {
				t.Fatal("fallback was skipped or retried")
			}
			if scenario == "fallback unavailable" {
				if preview.Name == nil || *preview.Name != "Primary gift" || preview.Price == nil || preview.Image != nil {
					t.Fatal("primary suggestions lost")
				}
				return
			}
			if preview.Name == nil || preview.Price == nil || preview.Image == nil || len(preview.Warnings) != 0 {
				t.Fatal("fallback did not complete suggestions")
			}
			if scenario == "blocked page" {
				if *preview.Name != "Alternative gift" {
					t.Fatal("fallback name lost")
				}
			} else if *preview.Name != "Primary gift" {
				t.Fatal("valid primary name overwritten")
			}
			if scenario == "blocked image" && strings.Join(paths, ",") != "/blocked,/photo" {
				t.Fatal("image fallback repeated an image or fetched a page")
			}
		})
	}
}

func TestPreview_WhenInitialDNSRejected_SkipsBothProviders(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("127.0.0.1")}}
	provider := fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		t.Fatal("provider received private destination")
		return importer.Metadata{}, nil
	}}
	client.PageSource, client.FallbackSource = provider, provider
	// Act / Assert
	if _, err := client.Preview(t.Context(), "https://merchant.fr/product"); err == nil {
		t.Fatal("private destination accepted")
	}
}
