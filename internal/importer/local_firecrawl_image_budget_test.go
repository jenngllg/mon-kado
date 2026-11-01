package importer

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"image"
	"image/png"
	"io"
	"net/http"
	"net/netip"
	"net/url"
	"testing"
	"testing/synctest"
	"time"
)

type alternativeFreeImage struct{ content []byte }

func (s alternativeFreeImage) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	if target.Path == "/alternative" {
		return s.content, ctx.Err()
	}
	return nil, errors.New("image unavailable")
}

func TestPreview_WhenAccountImageStalls_RespectsRollingAccountLimit(t *testing.T) {
	for _, quotaAvailable := range []bool{true, false} {
		t.Run(map[bool]string{true: "remaining capacity", false: "last available request"}[quotaAvailable], func(t *testing.T) {
			synctest.Test(t, func(t *testing.T) {
				// Arrange
				var picture bytes.Buffer
				if err := png.Encode(&picture, image.NewRGBA(image.Rect(0, 0, 2, 2))); err != nil {
					t.Fatal(err)
				}
				name, price, imageURL := "Gift", json.Number("12"), "https://merchant.fr/blocked"
				client := NewWithAdaptivePreviewCache(time.Now)
				client.Timeout = 8 * time.Second
				client.Resolver = &previewTestResolver{answers: [][]netip.Addr{{netip.MustParseAddr("1.1.1.1")}}}
				client.Dialer = previewTestUnavailableDialer{}
				client.PageSource = &previewTestPage{metadata: Metadata{Name: &name, Price: &price, Images: []string{imageURL}}}
				client.ImageSource = alternativeFreeImage{content: picture.Bytes()}
				account := NewLocalFirecrawlRolling(client, "test-provider-key", time.Now, false)
				if !quotaAvailable {
					for range keylessWindowLimit - 1 {
						account.rollingStarts = append(account.rollingStarts, time.Now())
					}
				}
				var formats []string
				account.request = func(ctx context.Context, _ *url.URL, _ string, body io.Reader, _ http.Header) (*http.Response, error) {
					var input firecrawlRequest
					if err := json.NewDecoder(body).Decode(&input); err != nil || len(input.Formats) != 1 {
						t.Fatal("invalid provider request")
					}
					formats = append(formats, input.Formats[0])
					if input.Formats[0] == "rawBase64" {
						<-ctx.Done()
						return nil, ctx.Err()
					}
					return firecrawlDocument(`<script type="application/ld+json">{"@type":"Product","name":"Gift","image":"/alternative","offers":{"price":"12","priceCurrency":"EUR"}}</script>`, "https://merchant.fr/product", "https://merchant.fr/product", 200), nil
				}
				client.FallbackSource = account
				client.FinalImageSource = NewLocalSingleImageAttempt(account)

				// Act
				started := time.Now()
				result, err := client.Preview(t.Context(), "https://merchant.fr/product")

				// Assert
				if err != nil || result.Name == nil || result.Price == nil || (result.Image != nil) != quotaAvailable || len(formats) == 0 || formats[0] != "rawBase64" || time.Since(started) >= client.Timeout {
					t.Fatalf("shared account failed to recover an alternative image: error=%v formats=%v elapsed=%v", err, formats, time.Since(started))
				}
				if quotaAvailable && (len(formats) != 2 || formats[1] != "rawHtml") || !quotaAvailable && len(formats) != 1 {
					t.Fatalf("shared account admission was not respected: formats=%v", formats)
				}
			})
		})
	}
}
