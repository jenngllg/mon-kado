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
)

func TestCloudflareRenderer_WhenBrowserReturnsImage_AcceptsOnlyBoundedDeclaredPublicProductBytes(t *testing.T) {
	for _, scenario := range []string{"declared image", "unrelated image", "active media", "invalid base64", "private image DNS", "excessive bytes"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			target, _ := url.Parse("https://merchant.fr/product")
			picture := map[string]string{"url": "https://assets.merchant.fr/photo.jpg", "contentType": "image/jpeg", "contentBase64": base64.StdEncoding.EncodeToString([]byte("untrusted image bytes"))}
			switch scenario {
			case "unrelated image":
				picture["url"] = "https://merchant.fr/unrelated"
			case "active media":
				picture["contentType"] = "image/svg+xml"
			case "invalid base64":
				picture["contentBase64"] = "!"
			case "excessive bytes":
				picture["contentBase64"] = strings.Repeat("A", base64.StdEncoding.EncodedLen(maximumRenderedImageBytes)+4)
			}
			document, _ := json.Marshal(map[string]any{"html": `<title>Gift</title><meta property="og:image" content="https://assets.merchant.fr/photo.jpg">`, "url": target.String(), "statusCode": 200, "image": picture})
			validations := 0
			client := &CloudflareRenderer{origin: "https://monkado-wish-render-local.account.workers.dev", validator: localDirectFake{validate: func(ctx context.Context, value string) error {
				validations++
				if scenario == "private image DNS" && validations == 3 {
					return rejected()
				}
				return ctx.Err()
			}}, request: func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				return relayResponse(200, string(document), "application/json"), nil
			}}
			// Act
			metadata, err := client.Get(t.Context(), target)
			// Assert
			if err != nil || metadata.Name == nil || *metadata.Name != "Gift" {
				t.Fatalf("optional image failure lost product text: %v", err)
			}
			if scenario == "declared image" {
				if string(metadata.ImageData) != "untrusted image bytes" || validations != 3 {
					t.Fatal("declared bytes or DNS validation lost")
				}
			} else if len(metadata.ImageData) != 0 {
				t.Fatal("unsupported picture bytes accepted")
			}
		})
	}
}
