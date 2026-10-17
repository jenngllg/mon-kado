package importer_test

import (
	"bufio"
	"bytes"
	"context"
	"errors"
	"image"
	"image/png"
	"io"
	"net"
	"net/http"
	"net/netip"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/importer"
)

type resolverFunc func(context.Context, string, string) ([]netip.Addr, error)

func (f resolverFunc) LookupNetIP(ctx context.Context, network, host string) ([]netip.Addr, error) {
	return f(ctx, network, host)
}

type dialerFunc func(context.Context, string, string) (net.Conn, error)

func (f dialerFunc) DialContext(ctx context.Context, network, address string) (net.Conn, error) {
	return f(ctx, network, address)
}

func pipeMerchant(t *testing.T, client *importer.Client, reply func(*http.Request) *http.Response) *int {
	t.Helper()
	calls := new(int)
	var running sync.WaitGroup
	t.Cleanup(running.Wait)
	client.Dialer = dialerFunc(func(ctx context.Context, network, address string) (net.Conn, error) {
		if address != "8.8.8.8:80" || network != "tcp" {
			return nil, errors.New("destination was not pinned to the validated public IP")
		}
		*calls++
		local, remote := net.Pipe()
		running.Go(func() {
			defer remote.Close()
			req, err := http.ReadRequest(bufio.NewReader(remote))
			if err != nil {
				t.Error(err)
				return
			}
			response := reply(req)
			response.Close = true
			if err = response.Write(remote); err != nil && !errors.Is(err, io.ErrClosedPipe) {
				t.Error(err)
			}
		})
		return local, nil
	})
	return calls
}

func TestDownload_WhenPublicHostRedirects_RevalidatesDNSAndPinsEachConnection(t *testing.T) {
	// Arrange
	t.Setenv("HTTP_PROXY", "http://127.0.0.1:1")
	client := importer.New()
	hosts := []string{}
	client.Resolver = resolverFunc(func(ctx context.Context, network, host string) ([]netip.Addr, error) {
		hosts = append(hosts, host)
		return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, ctx.Err()
	})
	calls := pipeMerchant(t, client, func(req *http.Request) *http.Response {
		if req.Host == "merchant.test" {
			return &http.Response{StatusCode: 302, Header: http.Header{"Location": []string{"http://other.test/product"}}, Body: io.NopCloser(strings.NewReader(""))}
		}
		return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": []string{"text/html; charset=utf-8"}}, Body: io.NopCloser(strings.NewReader("product"))}
	})
	// Act
	data, final, media, err := client.Download(t.Context(), "http://merchant.test/product", 100)
	// Assert
	if err != nil || string(data) != "product" || final.Host != "other.test" || media != "text/html" || *calls != 2 || len(hosts) != 2 || hosts[0] != "merchant.test" || hosts[1] != "other.test" {
		t.Fatalf("public redirect contract differs: %v", err)
	}
}

func TestDownload_WhenRedirectIsRelative_PreservesMerchantAuthorityAndRevalidatesItsAddress(t *testing.T) {
	// Arrange
	client := importer.New()
	hosts := []string{}
	client.Resolver = resolverFunc(func(ctx context.Context, network, host string) ([]netip.Addr, error) {
		hosts = append(hosts, host)
		return []netip.Addr{netip.MustParseAddr("8.8.8.8")}, ctx.Err()
	})
	calls := pipeMerchant(t, client, func(req *http.Request) *http.Response {
		if req.Host != "merchant.test" {
			t.Error("numeric transport destination replaced merchant Host")
		}
		if req.URL.Path == "/products/start" {
			return &http.Response{StatusCode: 302, Header: http.Header{"Location": []string{"../final?variant=123"}}, Body: io.NopCloser(strings.NewReader(""))}
		}
		if req.URL.Path != "/final" || req.URL.RawQuery != "variant=123" {
			t.Error("relative redirect path or variant changed")
		}
		return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": []string{"text/html"}}, Body: io.NopCloser(strings.NewReader("product"))}
	})
	// Act
	_, final, _, err := client.Download(t.Context(), "http://merchant.test/products/start", 100)
	// Assert
	if err != nil || final.String() != "http://merchant.test/final?variant=123" || *calls != 2 || len(hosts) != 2 || hosts[0] != "merchant.test" || hosts[1] != "merchant.test" {
		t.Fatal("relative redirect escaped original authority or connection validation")
	}
}

func TestPreview_WhenDNSRebindsAfterValidation_RefusesPrivateConnection(t *testing.T) {
	// Arrange
	client := importer.New()
	lookups, dials := 0, 0
	client.Resolver = resolverFunc(func(ctx context.Context, network, host string) ([]netip.Addr, error) {
		lookups++
		ip := "8.8.8.8"
		if lookups > 1 {
			ip = "127.0.0.1"
		}
		return []netip.Addr{netip.MustParseAddr(ip)}, nil
	})
	client.Dialer = dialerFunc(func(context.Context, string, string) (net.Conn, error) {
		dials++
		return nil, errors.New("must not dial")
	})
	// Act
	preview, err := client.Preview(t.Context(), "http://merchant.test/product")
	// Assert
	if err != nil || dials != 0 || lookups != 2 || preview.Name != nil || len(preview.Warnings) != 4 {
		t.Fatal("rebinding bypassed connection validation or best-effort warnings")
	}
}

func TestDownload_WhenResponseUnavailableOrTooLarge_RetriesOnlyTransientStatus(t *testing.T) {
	for _, mode := range []string{"errorStatus", "oversized", "redirectLimit", "badLocation", "privateRedirect"} {
		t.Run(mode, func(t *testing.T) {
			// Arrange
			client := importer.New()
			client.Wait = func(ctx context.Context, _ time.Duration) error { return ctx.Err() }
			client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
			client.MaximumRedirects = 0
			calls := pipeMerchant(t, client, func(*http.Request) *http.Response {
				status, body := 200, "123456"
				header := http.Header{}
				if mode == "errorStatus" {
					status = 500
				}
				if strings.HasSuffix(mode, "Location") || strings.HasSuffix(mode, "Limit") || mode == "privateRedirect" {
					status = 302
					header.Set("Location", "http://merchant.test/again")
				}
				if mode == "badLocation" {
					header.Set("Location", ":bad")
				}
				if mode == "privateRedirect" {
					header.Set("Location", "http://127.0.0.1/private")
				}
				return &http.Response{StatusCode: status, Header: header, Body: io.NopCloser(strings.NewReader(body))}
			})
			// Act
			_, _, _, err := client.Download(t.Context(), "http://merchant.test/product", 5)
			// Assert
			wantCalls := 1
			if mode == "errorStatus" {
				wantCalls = 3
			}
			if err == nil || *calls != wantCalls {
				t.Fatal("unavailable response was accepted or repeated")
			}
		})
	}
}

func TestPreview_WhenMerchantProvidesProductAndImage_ReturnsNormalizedSuggestions(t *testing.T) {
	// Arrange
	client := importer.New()
	client.Resolver = resolverFake{[]netip.Addr{netip.MustParseAddr("8.8.8.8")}}
	var imageData bytes.Buffer
	if err := png.Encode(&imageData, image.NewNRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	pipeMerchant(t, client, func(req *http.Request) *http.Response {
		if req.URL.Path == "/image.png" {
			return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": []string{"image/png"}}, Body: io.NopCloser(bytes.NewReader(imageData.Bytes()))}
		}
		return &http.Response{StatusCode: 200, Header: http.Header{"Content-Type": []string{"text/html"}}, Body: io.NopCloser(strings.NewReader(`<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/image.png","offers":{"price":"12.30","priceCurrency":"EUR"}}</script>`))}
	})
	// Act
	preview, err := client.Preview(t.Context(), "http://merchant.test/product")
	// Assert
	if err != nil || preview.Name == nil || *preview.Name != "Gift" || preview.Price == nil || preview.Price.String() != "12.3" || preview.Image == nil || preview.Image.ContentType != "image/webp" || len(preview.Warnings) != 0 {
		t.Fatal("merchant suggestions or normalized image differ")
	}
}
