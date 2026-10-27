package importer

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"strings"
	"testing"
)

type managedResolver func(context.Context, string, string) ([]netip.Addr, error)

func (r managedResolver) LookupNetIP(ctx context.Context, network, host string) ([]netip.Addr, error) {
	return r(ctx, network, host)
}

func TestCloudflare_WhenPermanentOriginUsed_ConfinesSecretAndValidatesMerchantBeforeAndAfter(t *testing.T) {
	for _, image := range []bool{false, true} {
		t.Run(map[bool]string{false: "page", true: "image"}[image], func(t *testing.T) {
			// Arrange
			provider := NewCloudflare(New(), "https://monkado-wish-import.account.workers.dev", "relay-secret")
			validations, calls := 0, 0
			provider.validator = localDirectFake{validate: func(ctx context.Context, value string) error {
				validations++
				if ctx.Err() != nil || value != "https://merchant.fr/product" {
					t.Fatal("merchant validation changed")
				}
				return nil
			}}
			provider.request = func(ctx context.Context, target *url.URL, method string, body io.Reader, headers http.Header) (*http.Response, error) {
				calls++
				path := "/preview"
				if image {
					path = "/image"
				}
				if target.String() != "https://monkado-wish-import.account.workers.dev"+path || method != "POST" || headers.Get("Authorization") != "Bearer relay-secret" || headers.Get("Cookie") != "" {
					t.Fatal("relay authority or credential boundary changed")
				}
				var input map[string]string
				if json.NewDecoder(body).Decode(&input) != nil || len(input) != 1 || input["url"] != "https://merchant.fr/product" {
					t.Fatal("merchant input changed")
				}
				if image {
					return relayResponse(200, "photo", "image/png"), nil
				}
				return relayResponse(200, "<title>Notebook</title>", "text/html"), nil
			}
			target, _ := url.Parse("https://merchant.fr/product")
			// Act / Assert
			if image {
				content, err := provider.GetImage(t.Context(), target)
				if err != nil || string(content) != "photo" {
					t.Fatalf("image lost: %v", err)
				}
			} else {
				result, err := provider.Get(t.Context(), target)
				if err != nil || result.Name == nil || *result.Name != "Notebook" {
					t.Fatalf("document lost: %v", err)
				}
			}
			if calls != 1 || validations != 2 {
				t.Fatal("unexpected request or DNS validation count")
			}
		})
	}
}

func TestCloudflare_WhenPermanentBoundaryFails_RejectsWithoutDirectPageFallback(t *testing.T) {
	for _, scenario := range []string{"invalid origin", "private provider DNS", "redirect", "rebound merchant"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			direct := New()
			direct.Resolver = managedResolver(func(context.Context, string, string) ([]netip.Addr, error) {
				return []netip.Addr{netip.MustParseAddr("127.0.0.1")}, nil
			})
			origin := "https://monkado-wish-import.account.workers.dev"
			if scenario == "invalid origin" {
				origin = "https://attacker.example"
			}
			provider := NewCloudflare(direct, origin, strings.Repeat("a", 64))
			validations, calls := 0, 0
			provider.validator = localDirectFake{validate: func(context.Context, string) error {
				validations++
				if scenario == "rebound merchant" && validations == 2 {
					return rejected()
				}
				return nil
			}}
			if scenario != "private provider DNS" {
				provider.request = func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
					calls++
					if scenario == "redirect" {
						r := relayResponse(302, "", "text/html")
						r.Header.Set("Location", "https://attacker.example")
						return r, nil
					}
					return relayResponse(200, "<title>Notebook</title>", "text/html"), nil
				}
			}
			target, _ := url.Parse("https://merchant.fr/product")
			// Act
			_, err := provider.Get(t.Context(), target)
			// Assert
			if err == nil {
				t.Fatal("unsafe relay response accepted")
			}
			if scenario == "invalid origin" && calls != 0 {
				t.Fatal("credential sent to an unapproved origin")
			}
			if calls > 1 {
				t.Fatal("permanent refusal retried")
			}
		})
	}
}

func TestCloudflare_WhenCallerCancelled_DoesNotSendPermanentRequest(t *testing.T) {
	// Arrange
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	provider := NewCloudflare(New(), "https://monkado-wish-import.account.workers.dev", "secret")
	target, _ := url.Parse("https://merchant.fr/product")
	// Act / Assert
	if _, err := provider.Get(ctx, target); !errors.Is(err, context.Canceled) {
		t.Fatal("caller cancellation lost")
	}
}

func TestPreview_WhenManagedRelayRejectsPublicNumericHost_DoesNotUseDirectPageAccess(t *testing.T) {
	// Arrange
	client := New()
	resolutions := 0
	client.Resolver = managedResolver(func(context.Context, string, string) ([]netip.Addr, error) {
		resolutions++
		return []netip.Addr{netip.MustParseAddr("93.184.216.34")}, nil
	})
	client.PageSource = NewCloudflare(client, "https://monkado-wish-import.account.workers.dev", "secret")
	// Act
	result, err := client.Preview(t.Context(), "https://93.184.216.34/product")
	// Assert
	if err != nil || result.Name != nil || result.Image != nil || resolutions != 1 {
		t.Fatal("unsupported managed destination bypassed the relay")
	}
}
