package importer

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"mime"
	"net/http"
	"net/url"
	"strings"

	"github.com/JennGllg/mon-kado/internal/images"
)

const localCloudflareImageEndpoint = "http://127.0.0.1:8787/image"

// GetImage uses one authenticated, bounded edge fetch after a direct image failure.
// Workers chooses its merchant IP; the caller must explicitly enable this local exception.
func (c *LocalCloudflare) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	if !c.CanHandle(target) {
		return nil, rejected()
	}
	clean := *target
	clean.Scheme, clean.Host = "https", strings.ToLower(clean.Hostname())
	clean.Fragment, clean.RawFragment = "", ""
	if err := c.validator.Validate(ctx, clean.String()); err != nil {
		return nil, err
	}
	budget, cancel := context.WithTimeout(ctx, localCloudflareBudget)
	defer cancel()
	body, _ := json.Marshal(struct {
		URL string `json:"url"`
	}{clean.String()})
	response, err := c.relayRequest(budget, "/image", body)
	if err != nil {
		return nil, errors.New("development image relay is unavailable")
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusOK {
		return nil, errors.New("development image relay is unavailable")
	}
	media, _, _ := mime.ParseMediaType(response.Header.Get("Content-Type"))
	switch media {
	case "image/jpeg", "image/png", "image/webp", "image/avif", "application/octet-stream":
	default:
		return nil, errors.New("development image media is unavailable")
	}
	content, err := io.ReadAll(io.LimitReader(response.Body, images.MaximumInputBytes+1))
	if err != nil || len(content) == 0 || len(content) > images.MaximumInputBytes {
		return nil, errors.New("development image body is unavailable")
	}
	if err := c.validator.Validate(budget, clean.String()); err != nil {
		return nil, err
	}
	return content, nil
}
