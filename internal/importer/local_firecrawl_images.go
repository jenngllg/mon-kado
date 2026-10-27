package importer

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"mime"
	"net/url"
	"time"

	"github.com/JennGllg/mon-kado/internal/images"
)

const firecrawlImageDecodeReserve = 500 * time.Millisecond

// GetImage retrieves the original response bytes through the local final provider.
// It shares admission, quota cooldown and credentials with page retrieval, and
// leaves actual pixel validation and normalization to the common image pipeline.
func (c *LocalFirecrawl) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	if !c.CanHandle(target) {
		return nil, rejected()
	}
	clean := *target
	clean.Scheme, clean.Host = "https", clean.Hostname()
	clean.Fragment, clean.RawFragment = "", ""
	if err := c.validator.Validate(ctx, clean.String()); err != nil {
		return nil, err
	}
	duration := localFirecrawlBudget
	if deadline, ok := ctx.Deadline(); ok {
		duration = min(duration, time.Until(deadline)-firecrawlImageDecodeReserve)
	}
	if duration < time.Second {
		return nil, errors.New("development image budget is unavailable")
	}
	budget, cancel := context.WithTimeout(ctx, duration)
	defer cancel()
	select {
	case c.slots <- struct{}{}:
		defer func() { <-c.slots }()
	case <-budget.Done():
		return nil, budget.Err()
	}
	if err := c.admit(budget); err != nil {
		return nil, err
	}
	deadline, _ := budget.Deadline()
	remaining := time.Until(deadline)
	if remaining < time.Second {
		return nil, errors.New("development image budget is unavailable")
	}
	body, _ := json.Marshal(firecrawlRequest{URL: clean.String(), Formats: []string{"rawBase64"}, OnlyMainContent: false, Proxy: "auto", Timeout: max(1000, remaining.Milliseconds()-250), SkipTLSVerification: false, Location: firecrawlLocation{Country: "FR", Languages: []string{"fr-FR", "fr"}}})
	encodedMaximum := base64.StdEncoding.EncodedLen(images.MaximumInputBytes)
	content, err := c.post(budget, body, encodedMaximum+65536)
	if err != nil {
		return nil, err
	}
	var envelope struct {
		Success bool
		Data    struct {
			Content  string `json:"rawBase64"`
			Metadata struct {
				Status    int    `json:"statusCode"`
				Media     string `json:"contentType"`
				SourceURL string `json:"sourceURL"`
				URL       string `json:"url"`
			}
		}
	}
	if json.Unmarshal(content, &envelope) != nil || !envelope.Success || envelope.Data.Metadata.Status != 200 || len(envelope.Data.Content) == 0 || len(envelope.Data.Content) > encodedMaximum {
		return nil, errors.New("development image document is unavailable")
	}
	media, _, _ := mime.ParseMediaType(envelope.Data.Metadata.Media)
	switch media {
	case "image/jpeg", "image/png", "image/webp", "image/avif", "application/octet-stream":
	default:
		return nil, errors.New("development image media is unavailable")
	}
	for _, value := range []string{clean.String(), envelope.Data.Metadata.SourceURL, envelope.Data.Metadata.URL} {
		if value == "" {
			continue
		}
		final, err := url.Parse(value)
		if err != nil || !c.CanHandle(final) || final.Scheme != "https" {
			return nil, rejected()
		}
		if err := c.validator.Validate(budget, final.String()); err != nil {
			return nil, err
		}
	}
	decoded, err := base64.StdEncoding.DecodeString(envelope.Data.Content)
	if err != nil || len(decoded) == 0 || len(decoded) > images.MaximumInputBytes {
		return nil, errors.New("development image body is unavailable")
	}
	if err := budget.Err(); err != nil {
		return nil, err
	}
	return decoded, nil
}
