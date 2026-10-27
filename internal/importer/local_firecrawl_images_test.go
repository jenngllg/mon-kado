package importer

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"io"
	"net/http"
	"net/url"
	"strings"
	"testing"
	"time"
)

func firecrawlImageDocument(content, media, final string, origin int) *http.Response {
	body, _ := json.Marshal(map[string]any{"success": true, "data": map[string]any{"rawBase64": content, "metadata": map[string]any{"statusCode": origin, "contentType": media, "url": final}}})
	return relayResponse(200, string(body), "application/json")
}

func TestLocalFirecrawlImage_WhenOriginalBytesReturned_UsesOneBinaryFormatAndSharedPageAdmission(t *testing.T) {
	// Arrange
	calls := 0
	var waits []time.Duration
	client := firecrawlFixture(t, func(ctx context.Context, target *url.URL, method string, body io.Reader, headers http.Header) (*http.Response, error) {
		calls++
		var input firecrawlRequest
		if json.NewDecoder(body).Decode(&input) != nil || headers.Get("Authorization") != "Bearer test-provider-key" || headers.Get("Cookie") != "" || target.String() != localFirecrawlEndpoint || method != "POST" {
			t.Fatal("provider boundary changed")
		}
		if calls == 1 {
			return firecrawlDocument(`<title>Gift</title>`, "", "", 200), nil
		}
		if len(input.Formats) != 1 || input.Formats[0] != "rawBase64" || input.URL != "https://media.merchant.fr/photo" || input.SkipTLSVerification {
			t.Fatal("binary request changed")
		}
		return firecrawlImageDocument(base64.StdEncoding.EncodeToString([]byte("original-image-bytes")), "image/png", "https://media.merchant.fr/photo", 200), nil
	})
	client.wait = func(ctx context.Context, d time.Duration) error { waits = append(waits, d); return ctx.Err() }
	page, _ := url.Parse("https://merchant.fr/product")
	photo, _ := url.Parse("http://media.merchant.fr:80/photo#private")
	// Act
	_, first := client.Get(t.Context(), page)
	content, err := client.GetImage(t.Context(), photo)
	// Assert
	if first != nil || err != nil || string(content) != "original-image-bytes" || calls != 2 || len(waits) != 1 || waits[0] != firecrawlStartInterval {
		t.Fatalf("shared admission or bytes differ %v %v", first, err)
	}
}

func TestLocalFirecrawlImage_WhenResponseUnsafe_RejectsWithoutRetryOrSecretReflection(t *testing.T) {
	for _, scenario := range []string{"private initial", "private final", "HTTP final", "status", "media", "base64", "oversized", "empty", "quota", "malformed"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			calls := 0
			target, _ := url.Parse("https://media.merchant.fr/photo")
			client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				if scenario == "quota" {
					return relayResponse(429, "secret-provider-body", "application/json"), nil
				}
				if scenario == "malformed" {
					return relayResponse(200, "secret-provider-body", "application/json"), nil
				}
				body, media, final, status := base64.StdEncoding.EncodeToString([]byte("image")), "image/webp", "https://media.merchant.fr/photo", 200
				switch scenario {
				case "private final":
					final = "https://127.0.0.1/"
				case "HTTP final":
					final = "http://media.merchant.fr/"
				case "status":
					status = 403
				case "media":
					media = "text/html"
				case "base64":
					body = "!!secret-provider-body"
				case "oversized":
					body = strings.Repeat("a", base64.StdEncoding.EncodedLen(10*1024*1024)+4)
				case "empty":
					body = ""
				}
				return firecrawlImageDocument(body, media, final, status), nil
			})
			if scenario == "private initial" {
				target, _ = url.Parse("https://127.0.0.1/")
			}
			// Act
			content, err := client.GetImage(t.Context(), target)
			// Assert
			if err == nil || content != nil || calls > 1 || strings.Contains(err.Error(), "secret-provider-body") {
				t.Fatal("unsafe body accepted, retried or reflected")
			}
			if scenario == "private initial" && calls != 0 {
				t.Fatal("private image sent to provider")
			}
		})
	}
}
