package importer_test

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"image"
	"image/png"
	"io"
	"net"
	"net/http"
	"net/netip"
	"net/url"
	"reflect"
	"testing"
	"testing/synctest"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

type stalledRenderedImage struct{ calls int }

type delayedFreeImage struct {
	content []byte
	calls   int
}

type delayedAccountImage struct{ content []byte }
type slowSecondFreeImage struct{ firstCalls int }

func TestPreview_WhenPrimaryReturnsInterstitial_UsesProductFromFallback(t *testing.T) {
	// Arrange
	var picture bytes.Buffer
	if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.PageSource = fallbackPage{get: func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		markup := `<h1>Pardon Our Interruption</h1><script>window.reeseSkipExpirationCheck = true;</script>`
		return importer.Extract([]byte(markup), target), nil
	}}
	name, price, calls := "Product from fallback", json.Number("12.50"), 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		calls++
		return importer.Metadata{Name: &name, Price: &price, ImageData: picture.Bytes()}, nil
	}}
	// Act
	result, err := client.Preview(t.Context(), "https://merchant.test/product")
	// Assert
	if err != nil || calls != 1 || result.Name == nil || *result.Name != name || result.Price == nil || *result.Price != price || result.Image == nil {
		t.Fatalf("fallback product was not retained: %+v, calls=%d, err=%v", result, calls, err)
	}
}

func TestPreview_WhenPrimaryReturnsMaintenancePage_UsesProductFromFallback(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.PageSource = fallbackPage{get: func(_ context.Context, target *url.URL) (importer.Metadata, error) {
		return importer.Extract([]byte(`<title>Example - Maintenance</title><body>Malheureusement notre site n'est actuellement pas disponible.</body>`), target), nil
	}}
	name, calls := "Product from fallback", 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		calls++
		return importer.Metadata{Name: &name}, nil
	}}
	// Act
	result, err := client.Preview(t.Context(), "https://merchant.test/product")
	// Assert
	if err != nil || calls != 1 || result.Name == nil || *result.Name != name {
		t.Fatalf("fallback product was not retained: calls=%d, err=%v", calls, err)
	}
}

func (s *slowSecondFreeImage) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	if target.Path == "/first" {
		s.firstCalls++
		if s.firstCalls > 1 {
			return nil, errors.New("first unavailable")
		}
	}
	<-ctx.Done()
	return nil, ctx.Err()
}

func (s delayedAccountImage) GetImage(ctx context.Context, _ *url.URL) ([]byte, error) {
	select {
	case <-time.After(2500 * time.Millisecond):
		return s.content, nil
	case <-ctx.Done():
		return nil, ctx.Err()
	}
}

func TestPreview_WhenFreeImageStalls_LeavesTimeForSlowAccountImage(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		var picture bytes.Buffer
		if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
			t.Fatal(err)
		}
		client := importer.NewWithAdaptivePreviewCache(time.Now)
		client.Timeout = 8 * time.Second
		client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
		client.Dialer = dialerFunc(func(context.Context, string, string) (net.Conn, error) { return nil, errors.New("unavailable") })
		name, price := "Gift", json.Number("12")
		client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			return importer.Metadata{Name: &name, Price: &price, Images: []string{"https://merchant.test/first", "https://merchant.test/second"}}, nil
		}}
		client.ImageSource = &slowSecondFreeImage{}
		client.FinalImageSource = delayedAccountImage{content: picture.Bytes()}
		client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			return importer.Metadata{}, errors.New("unavailable")
		}}

		// Act
		result, err := client.Preview(t.Context(), "https://merchant.test/product")

		// Assert
		if err != nil || result.Image == nil {
			t.Fatalf("free-image retry consumed the account image budget: %v", err)
		}
	})
}

func (s *delayedFreeImage) GetImage(ctx context.Context, _ *url.URL) ([]byte, error) {
	s.calls++
	select {
	case <-time.After(1500 * time.Millisecond):
		return s.content, nil
	case <-ctx.Done():
		return nil, ctx.Err()
	}
}

func TestPreview_WhenOnlyFreeImageNeedsMoreTime_AvoidsBrowserAndMeteredFallback(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		var picture bytes.Buffer
		if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
			t.Fatal(err)
		}
		client := importer.NewWithAdaptivePreviewCache(time.Now)
		client.Timeout = 8 * time.Second
		client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
		client.Dialer = dialerFunc(func(context.Context, string, string) (net.Conn, error) {
			return nil, errors.New("direct image unavailable")
		})
		client.PageSource = fallbackPage{get: func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
			markup := `<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/photo","offers":{"price":"12","priceCurrency":"EUR"}}</script>`
			return importer.Extract([]byte(markup), target), ctx.Err()
		}}
		imageSource := &delayedFreeImage{content: picture.Bytes()}
		client.ImageSource = imageSource
		browserCalls, fallbackCalls := 0, 0
		client.RenderedSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			browserCalls++
			return importer.Metadata{}, errors.New("unexpected browser")
		}}
		client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			fallbackCalls++
			return importer.Metadata{}, errors.New("unexpected paid fallback")
		}}

		// Act
		result, err := client.Preview(t.Context(), "https://merchant.test/product")

		// Assert
		if err != nil || result.Name == nil || result.Price == nil || result.Image == nil || imageSource.calls != 1 || browserCalls != 0 || fallbackCalls != 0 {
			t.Fatalf("free image recovery did not avoid unnecessary providers: %v", err)
		}
	})
}

func TestPreview_WhenFreeImageStalls_ReservesFallbackBudget(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client := importer.NewWithAdaptivePreviewCache(time.Now)
		client.Timeout = 4 * time.Second
		client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
		client.Dialer = dialerFunc(func(context.Context, string, string) (net.Conn, error) {
			return nil, errors.New("direct image unavailable")
		})
		client.PageSource = fallbackPage{get: func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
			markup := `<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/photo","offers":{"price":"12","priceCurrency":"EUR"}}</script>`
			return importer.Extract([]byte(markup), target), ctx.Err()
		}}
		client.ImageSource = &stalledRenderedImage{}
		client.RenderedSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			return importer.Metadata{}, errors.New("browser unavailable")
		}}
		fallbackCalls := 0
		client.FallbackSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
			fallbackCalls++
			deadline, ok := ctx.Deadline()
			if !ok || time.Until(deadline) < 3*time.Second {
				t.Fatal("the stalled image consumed the provider reserve")
			}
			return importer.Metadata{}, errors.New("fallback unavailable")
		}}

		// Act
		result, err := client.Preview(t.Context(), "https://merchant.test/product")

		// Assert
		if err != nil || result.Name == nil || result.Price == nil || result.Image != nil || fallbackCalls != 1 {
			t.Fatalf("stalled image prevented the fallback: %v", err)
		}
	})
}

func TestPreview_WhenFinalImageStalls_ReservesTimeForAlternativeImage(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		var picture bytes.Buffer
		if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
			t.Fatal(err)
		}
		client := importer.NewWithAdaptivePreviewCache(time.Now)
		client.Timeout = 8 * time.Second
		client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
		client.PageSource = fallbackPage{get: func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
			return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/blocked","offers":{"price":"12","priceCurrency":"EUR"}}</script>`), target), ctx.Err()
		}}
		final := &stalledFinalImage{}
		client.FinalImageSource = final
		fallbackCalls := 0
		client.FallbackSource = fallbackPage{get: func(ctx context.Context, target *url.URL) (importer.Metadata, error) {
			fallbackCalls++
			deadline, ok := ctx.Deadline()
			if !ok || time.Until(deadline) < 3*time.Second {
				t.Fatal("final image consumed the fallback reserve")
			}
			return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/alternative","offers":{"price":"12","priceCurrency":"EUR"}}</script>`), target), nil
		}}
		pipeMerchant(t, client, func(request *http.Request) *http.Response {
			if request.URL.Path == "/alternative" {
				return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": {"image/png"}}, Body: io.NopCloser(bytes.NewReader(picture.Bytes()))}
			}
			return &http.Response{StatusCode: 403, Header: http.Header{}, Body: io.NopCloser(bytes.NewReader(nil))}
		})

		// Act
		result, err := client.Preview(t.Context(), "http://merchant.test/product")

		// Assert
		if err != nil || result.Name == nil || result.Price == nil || result.Image == nil || final.calls != 1 || fallbackCalls != 1 {
			t.Fatalf("stalled final image blocked alternative: error %v, final calls %d, fallback calls %d", err, final.calls, fallbackCalls)
		}
	})
}

type stalledFinalImage struct{ calls int }

func (s *stalledFinalImage) GetImage(ctx context.Context, _ *url.URL) ([]byte, error) {
	s.calls++
	<-ctx.Done()
	return nil, ctx.Err()
}

func (s *stalledRenderedImage) GetImage(ctx context.Context, _ *url.URL) ([]byte, error) {
	s.calls++
	if s.calls == 1 {
		<-ctx.Done()
		return nil, ctx.Err()
	}
	return nil, errors.New("unavailable")
}

func TestPreview_WhenBrowserStallsWithOnlyPriceMissing_ReservesTimeForFallback(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		var picture bytes.Buffer
		if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
			t.Fatal(err)
		}
		name := "Gift"
		client := importer.New()
		client.Timeout = 8 * time.Second
		client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
		client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			return importer.Metadata{Name: &name, ImageData: picture.Bytes()}, nil
		}}
		browserCalls, fallbackCalls := 0, 0
		client.RenderedSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
			browserCalls++
			<-ctx.Done()
			return importer.Metadata{}, ctx.Err()
		}}
		client.FallbackSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
			fallbackCalls++
			deadline, ok := ctx.Deadline()
			if !ok || time.Until(deadline) < 3*time.Second {
				t.Fatal("browser consumed the fallback reserve")
			}
			base, _ := url.Parse("https://merchant.test/product")
			return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base), nil
		}}

		// Act
		result, err := client.Preview(t.Context(), "https://merchant.test/product")

		// Assert
		if err != nil || browserCalls != 1 || fallbackCalls != 1 || result.Price == nil || result.Image == nil || len(result.Warnings) != 0 {
			t.Fatalf("price fallback did not recover the preview: %v", err)
		}
	})
}

func TestPreview_WhenBrowserStallsWithoutProductFields_ReservesTimeForFallback(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client := importer.New()
		client.Timeout = 8 * time.Second
		client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
		client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			return importer.Metadata{}, errors.New("unavailable")
		}}
		browserCalls, fallbackCalls := 0, 0
		client.RenderedSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
			browserCalls++
			<-ctx.Done()
			return importer.Metadata{}, ctx.Err()
		}}
		client.FallbackSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
			fallbackCalls++
			deadline, ok := ctx.Deadline()
			if !ok || time.Until(deadline) < 3*time.Second {
				t.Fatal("browser consumed the fallback reserve")
			}
			select {
			case <-time.After(2500 * time.Millisecond):
			case <-ctx.Done():
				return importer.Metadata{}, ctx.Err()
			}
			base, _ := url.Parse("https://merchant.test/product")
			return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base), nil
		}}

		// Act
		result, err := client.Preview(t.Context(), "https://merchant.test/product")

		// Assert
		if err != nil || browserCalls != 1 || fallbackCalls != 1 || result.Name == nil || result.Price == nil || result.Image != nil || len(result.Warnings) != 1 || result.Warnings[0] != "WISH_IMPORT_IMAGE_UNAVAILABLE" {
			t.Fatalf("fallback did not recover the missing fields: %v; warnings %v", err, result.Warnings)
		}
	})
}

func TestPreview_WhenRenderedImageStalls_ReservesTimeForFallback(t *testing.T) {
	synctest.Test(t, func(t *testing.T) {
		// Arrange
		client := importer.New()
		client.Timeout = 8 * time.Second
		client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
		client.Dialer = dialerFunc(func(context.Context, string, string) (net.Conn, error) {
			return nil, errors.New("unavailable")
		})
		client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			return importer.Metadata{}, errors.New("unavailable")
		}}
		client.RenderedSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
			base, _ := url.Parse("https://merchant.test/product")
			return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/photo"}</script>`), base), nil
		}}
		image := &stalledRenderedImage{}
		client.ImageSource = image
		fallbackCalls := 0
		client.FallbackSource = fallbackPage{get: func(ctx context.Context, _ *url.URL) (importer.Metadata, error) {
			fallbackCalls++
			deadline, ok := ctx.Deadline()
			if !ok || time.Until(deadline) < 3*time.Second {
				t.Fatal("image download consumed the fallback reserve")
			}
			base, _ := url.Parse("https://merchant.test/product")
			return importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`), base), nil
		}}

		// Act
		result, err := client.Preview(t.Context(), "https://merchant.test/product")

		// Assert
		if err != nil || fallbackCalls != 1 || image.calls != 2 || result.Name == nil || result.Price == nil || result.Image != nil || len(result.Warnings) != 1 || result.Warnings[0] != "WISH_IMPORT_IMAGE_UNAVAILABLE" {
			t.Fatalf("stalled image prevented fallback or retry: %v; warnings %v", err, result.Warnings)
		}
	})
}

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
			if scenario == "failed browser" {
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

func TestPreview_WhenPrimaryCurrencyIsUnsupported_FinalProviderCannotReplaceItsPrice(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	base, _ := url.Parse("https://merchant.test/product")
	primary := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"price":"12","priceCurrency":"USD"}}</script>`), base)
	fallback := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","offers":{"price":"12","priceCurrency":"EUR"}}</script>`), base)
	client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) { return primary, nil }}
	calls := 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		calls++
		return fallback, nil
	}}

	// Act
	preview, err := client.Preview(t.Context(), base.String())

	// Assert
	if err != nil || calls != 1 || preview.Price != nil {
		t.Fatalf("unsupported currency was replaced: price %v, fallback calls %d, error %v", preview.Price, calls, err)
	}
	found := false
	for _, warning := range preview.Warnings {
		found = found || warning == "WISH_IMPORT_CURRENCY_UNSUPPORTED"
	}
	if !found {
		t.Fatal("unsupported currency warning was lost")
	}
}

func TestPreview_WhenKnownPriceCannotBeUsed_DoesNotSpendFallbackOnPriceAlone(t *testing.T) {
	for _, scenario := range []struct {
		name        string
		ambiguous   bool
		unsupported bool
	}{
		{"unselected variant", true, false},
		{"unsupported currency", false, true},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			var picture bytes.Buffer
			if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
				t.Fatal(err)
			}
			name := "Gift"
			client := importer.New()
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
				return importer.Metadata{Name: &name, ImageData: picture.Bytes(), PriceAmbiguous: scenario.ambiguous, UnsupportedCurrency: scenario.unsupported}, nil
			}}
			client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
				t.Fatal("paid fallback attempted to guess an unusable price")
				return importer.Metadata{}, nil
			}}

			// Act
			preview, err := client.Preview(t.Context(), "https://merchant.test/product")

			// Assert
			if err != nil || preview.Name == nil || preview.Image == nil || preview.Price != nil || len(preview.Warnings) != 1 {
				t.Fatalf("partial preview changed: error %v, warnings %v", err, preview.Warnings)
			}
		})
	}
}

func TestPreview_WhenAmbiguousPriceAndImageAreMissing_RecoversImageWithoutGuessingPrice(t *testing.T) {
	// Arrange
	var picture bytes.Buffer
	if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	base, _ := url.Parse("http://merchant.test/product")
	name := "Gift"
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	client.PageSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		return importer.Metadata{Name: &name, PriceAmbiguous: true}, nil
	}}
	alt := importer.Extract([]byte(`<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/photo","offers":{"price":"12","priceCurrency":"EUR"}}</script>`), base)
	calls := 0
	client.FallbackSource = fallbackPage{get: func(context.Context, *url.URL) (importer.Metadata, error) {
		calls++
		return alt, nil
	}}
	pipeMerchant(t, client, func(*http.Request) *http.Response {
		return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": {"image/png"}}, Body: io.NopCloser(bytes.NewReader(picture.Bytes()))}
	})

	// Act
	preview, err := client.Preview(t.Context(), base.String())

	// Assert
	if err != nil || calls != 1 || preview.Image == nil || preview.Price != nil {
		t.Fatalf("image recovery changed variant price: error %v, calls %d, price %v", err, calls, preview.Price)
	}
}
