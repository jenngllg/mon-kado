package importer

import (
	"context"
	"errors"
	"net/url"
	"time"
)

// LocalFirecrawlKeylessFirst tries Firecrawl's rate-limited keyless service
// before the authenticated account. It does not rotate or disguise IPs.
type LocalFirecrawlKeylessFirst struct {
	keyless, authenticated *LocalFirecrawl
	accountImages          bool
}

// NewLocalFirecrawlKeylessWithAccountImages enables one authenticated image
// attempt per preview when the free image service is unavailable transiently.
func NewLocalFirecrawlKeylessWithAccountImages(client *Client, account *LocalFirecrawl, now func() time.Time) *LocalFirecrawlKeylessFirst {
	provider := NewLocalFirecrawlKeylessFirst(client, account, now)
	provider.accountImages = true
	return provider
}

// NewLocalFirecrawlKeylessFirst creates an optional local experiment with an
// independent authenticated fallback when the keyless quota is unavailable.
func NewLocalFirecrawlKeylessFirst(client *Client, account *LocalFirecrawl, now func() time.Time) *LocalFirecrawlKeylessFirst {
	anonymous := NewLocalFirecrawl(client, "", now)
	anonymous.product = account.product
	return &LocalFirecrawlKeylessFirst{keyless: anonymous, authenticated: account}
}

// CanHandle accepts the same public URL shapes as Firecrawl.
func (c *LocalFirecrawlKeylessFirst) CanHandle(target *url.URL) bool {
	return c.keyless.CanHandle(target)
}

// Get falls back when the keyless tier rejects quota or reports a transient error.
func (c *LocalFirecrawlKeylessFirst) Get(ctx context.Context, target *url.URL) (Metadata, error) {
	result, err := c.keyless.Get(ctx, target)
	if keylessFallbackNeeded(ctx, err) {
		return c.authenticated.Get(ctx, target)
	}
	return result, err
}

// GetHTML applies the same fallback when optional product data is not requested.
func (c *LocalFirecrawlKeylessFirst) GetHTML(ctx context.Context, target *url.URL) (Metadata, error) {
	result, err := c.keyless.GetHTML(ctx, target)
	if keylessFallbackNeeded(ctx, err) {
		return c.authenticated.GetHTML(ctx, target)
	}
	return result, err
}

// GetImage retrieves image bytes without spending authenticated account credits.
func (c *LocalFirecrawlKeylessFirst) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	return c.keyless.GetImage(ctx, target)
}

func (c *LocalFirecrawlKeylessFirst) newPreviewImageSource() ImageSource {
	return &keylessPreviewImageSource{provider: c}
}

type keylessPreviewImageSource struct {
	provider       *LocalFirecrawlKeylessFirst
	accountAttempt bool
}

func (s *keylessPreviewImageSource) GetImage(ctx context.Context, target *url.URL) ([]byte, error) {
	content, err := s.provider.keyless.GetImage(ctx, target)
	if !s.provider.accountImages || s.accountAttempt || !keylessFallbackNeeded(ctx, err) {
		return content, err
	}
	s.accountAttempt = true
	return s.provider.authenticated.GetImage(ctx, target)
}

func keylessFallbackNeeded(ctx context.Context, err error) bool {
	return ctx.Err() == nil && (errors.Is(err, errKeylessQuota) || errors.Is(err, errKeylessBusy) || errors.Is(err, errKeylessTransient))
}
