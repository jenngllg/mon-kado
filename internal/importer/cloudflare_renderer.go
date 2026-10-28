package importer

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/config"
)

// The Worker itself stops after six seconds; transport and cleanup share one more.
const cloudflareRenderBudget = 7 * time.Second
const renderImageReserve = time.Second
const maximumRenderedImageBytes = 2 * 1024 * 1024

// CloudflareRenderer retrieves bounded anonymous HTML from a dedicated browser Worker.
// Its composition is restricted to explicitly enabled local experiments.
type CloudflareRenderer struct {
	validator    localURLValidator
	request      providerRequest
	origin, key  string
	maximumBytes int
}

// NewLocalCloudflareRenderer creates a DNS-pinned, authenticated browser provider.
// Merchant subrequests are guarded by the Worker under the managed-network exception.
func NewLocalCloudflareRenderer(client *Client, origin, key string) *CloudflareRenderer {
	return &CloudflareRenderer{validator: client, origin: origin, key: key, maximumBytes: client.MaximumHTMLBytes,
		request: func(ctx context.Context, target *url.URL, method string, body io.Reader, headers http.Header) (*http.Response, error) {
			return client.requestWithProtocol(ctx, target, method, body, headers, false)
		}}
}

// CanHandle accepts public web URL shapes; Get revalidates their full DNS sets.
func (c *CloudflareRenderer) CanHandle(target *url.URL) bool { return cloudflareURL(target) }

// Get renders one public product without retries, retaining the caller's deadline.
func (c *CloudflareRenderer) Get(ctx context.Context, target *url.URL) (Metadata, error) {
	if err := ctx.Err(); err != nil {
		return Metadata{}, err
	}
	if !c.CanHandle(target) {
		return Metadata{}, rejected()
	}
	if err := c.validator.Validate(ctx, target.String()); err != nil {
		return Metadata{}, err
	}
	origin, err := config.NormalizeCloudflareRenderOrigin(c.origin)
	if err != nil {
		return Metadata{}, errors.New("browser relay configuration is unavailable")
	}
	duration := cloudflareRenderBudget
	if deadline, ok := ctx.Deadline(); ok {
		duration = min(duration, time.Until(deadline)-renderImageReserve)
	}
	if duration <= 0 {
		return Metadata{}, context.DeadlineExceeded
	}
	budget, cancel := context.WithTimeout(ctx, duration)
	defer cancel()
	clean := *target
	clean.Scheme = "https"
	clean.Host = strings.ToLower(clean.Hostname())
	clean.Fragment, clean.RawFragment = "", ""
	input, _ := json.Marshal(struct {
		URL string `json:"url"`
	}{clean.String()})
	endpoint, _ := url.Parse(origin + "/render")
	response, err := c.request(budget, endpoint, http.MethodPost, bytes.NewReader(input), http.Header{"Authorization": {"Bearer " + c.key}, "Content-Type": {"application/json"}})
	if err != nil {
		if budget.Err() != nil {
			return Metadata{}, budget.Err()
		}
		return Metadata{}, errors.New("browser relay is unavailable")
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusOK || strings.Split(response.Header.Get("Content-Type"), ";")[0] != "application/json" {
		return Metadata{}, errors.New("browser document is unavailable")
	}
	maximum := c.maximumBytes
	if maximum <= 0 {
		maximum = config.MaximumImportHTMLBytes
	}
	envelopeMaximum := 2*maximum + 32768 + base64.StdEncoding.EncodedLen(maximumRenderedImageBytes)
	content, err := io.ReadAll(io.LimitReader(response.Body, int64(envelopeMaximum)+1))
	if err != nil || len(content) > envelopeMaximum {
		return Metadata{}, errors.New("browser document exceeds supported limits")
	}
	var document struct {
		HTML       string `json:"html"`
		URL        string `json:"url"`
		StatusCode int    `json:"statusCode"`
		Image      *struct {
			URL           string `json:"url"`
			ContentType   string `json:"contentType"`
			ContentBase64 string `json:"contentBase64"`
		} `json:"image"`
	}
	if json.Unmarshal(content, &document) != nil || document.StatusCode != 200 || len(document.HTML) == 0 || len(document.HTML) > maximum {
		return Metadata{}, errors.New("browser document is unavailable")
	}
	final, err := url.Parse(document.URL)
	if err != nil || final.User != nil || final.String() != clean.String() {
		return Metadata{}, errors.New("browser product identity changed")
	}
	if err := c.validator.Validate(budget, document.URL); err != nil {
		return Metadata{}, err
	}
	metadata := Extract([]byte(document.HTML), final)
	if picture := document.Image; picture != nil && len(picture.ContentBase64) <= base64.StdEncoding.EncodedLen(maximumRenderedImageBytes) {
		for _, candidate := range metadata.Images {
			if candidate != picture.URL {
				continue
			}
			if c.validator.Validate(budget, candidate) != nil {
				break
			}
			switch picture.ContentType {
			case "image/jpeg", "image/png", "image/webp", "image/avif":
				content, err := base64.StdEncoding.DecodeString(picture.ContentBase64)
				if err == nil && len(content) <= maximumRenderedImageBytes {
					metadata.ImageData = content
				}
			}
			break
		}
	}
	return metadata, nil
}
