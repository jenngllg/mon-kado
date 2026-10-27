package importer

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/url"
	"strings"
	"testing"
)

func TestLocalCloudflareImage_WhenPublicPhotoReturned_AuthenticatesAndValidatesBeforeAndAfter(t *testing.T) {
	// Arrange
	validations, calls := 0, 0
	client := NewLocalCloudflare(New(), "relay-key")
	client.validator = localDirectFake{validate: func(ctx context.Context, value string) error {
		validations++
		if value != "https://media.merchant.fr/photo?size=10" || ctx.Err() != nil {
			t.Fatal("validation lost clean identity or context")
		}
		return nil
	}}
	client.http.Transport = localRelayTransport(func(r *http.Request) (*http.Response, error) {
		calls++
		var input map[string]string
		if r.URL.String() != localCloudflareImageEndpoint || r.Method != "POST" || r.Header.Get("Authorization") != "Bearer relay-key" || r.Header.Get("Cookie") != "" || json.NewDecoder(r.Body).Decode(&input) != nil || len(input) != 1 || input["url"] != "https://media.merchant.fr/photo?size=10" {
			t.Fatal("image relay boundary changed")
		}
		return relayResponse(200, "bounded-image", "image/png"), nil
	})
	target, _ := url.Parse("http://MEDIA.MERCHANT.FR:80/photo?size=10#private")
	// Act
	content, err := client.GetImage(t.Context(), target)
	// Assert
	if err != nil || string(content) != "bounded-image" || calls != 1 || validations != 2 {
		t.Fatalf("content lost or repeated: %v %d %d", err, calls, validations)
	}
}

func TestLocalCloudflareImage_WhenUnsafeOrInvalidResponse_RejectsWithoutRetry(t *testing.T) {
	for _, scenario := range []string{"private URL", "private DNS", "rebound DNS", "cancelled", "network", "redirect", "media", "empty", "oversized"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			calls, validations := 0, 0
			client := NewLocalCloudflare(New(), "key")
			ctx := t.Context()
			target, _ := url.Parse("https://media.merchant.fr/photo")
			client.validator = localDirectFake{validate: func(context.Context, string) error {
				validations++
				if scenario == "private DNS" || scenario == "rebound DNS" && validations == 2 {
					return rejected()
				}
				return nil
			}}
			client.http.Transport = localRelayTransport(func(*http.Request) (*http.Response, error) {
				calls++
				switch scenario {
				case "network":
					return nil, errors.New("private-provider-secret")
				case "redirect":
					return relayResponse(302, "", "image/png"), nil
				case "media":
					return relayResponse(200, "private-provider-secret", "text/html"), nil
				case "empty":
					return relayResponse(200, "", "image/jpeg"), nil
				case "oversized":
					return relayResponse(200, strings.Repeat("x", 10*1024*1024+1), "image/jpeg"), nil
				default:
					return relayResponse(200, "image", "image/webp"), nil
				}
			})
			if scenario == "private URL" {
				target, _ = url.Parse("https://127.0.0.1/")
			}
			if scenario == "cancelled" {
				var cancel context.CancelFunc
				ctx, cancel = context.WithCancel(ctx)
				cancel()
			}
			// Act
			content, err := client.GetImage(ctx, target)
			// Assert
			if err == nil || content != nil || strings.Contains(err.Error(), "private-provider-secret") || calls > 1 {
				t.Fatal("unsafe image accepted, retried or reflected")
			}
			if (scenario == "private URL" || scenario == "private DNS" || scenario == "cancelled") && calls != 0 {
				t.Fatal("rejected URL reached relay")
			}
		})
	}
}
