package importer

import (
	"context"
	"crypto/sha256"
	"encoding/base64"
	"net/url"
	"strings"
	"sync"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

const previewCacheLifetime = 10 * time.Minute
const maximumPriceFreshness = 48 * time.Hour
const providerFreshnessSafety = 10 * time.Second
const maximumPreviewCacheEntries = 128
const maximumPreviewCacheBytes = 16 * 1024 * 1024
const maximumPendingPreviewKeys = 64

type previewKey [sha256.Size]byte

type previewEntry struct {
	value   domain.WishImportPreview
	image   []byte
	expires time.Time
	staleAt time.Time
	order   uint64
	size    int
}

type previewCache struct {
	mu       sync.Mutex
	now      func() time.Time
	entries  map[previewKey]previewEntry
	pending  map[previewKey]chan struct{}
	bytes    int
	order    uint64
	adaptive bool
}

// NewWithPreviewCache reuses complete public-product previews and previews with
// an explicitly ambiguous price for ten minutes. The bounded process-local
// cache stores no failures or request URL strings.
// Every caller still validates public DNS and keeps its own cancellation budget.
func NewWithPreviewCache(now func() time.Time) *Client {
	if now == nil {
		panic("preview cache clock is required")
	}
	client := New()
	client.cache = &previewCache{now: now, entries: make(map[previewKey]previewEntry), pending: make(map[previewKey]chan struct{})}
	return client
}

// NewWithAdaptivePreviewCache permits bounded reuse of prices for
// up to 48 hours after observation, or less when a provider reports their age.
func NewWithAdaptivePreviewCache(now func() time.Time) *Client {
	client := NewWithPreviewCache(now)
	client.cache.adaptive = true
	return client
}

func cacheKey(value string) previewKey {
	target, _ := url.Parse(value) // The caller has already validated the URL.
	target.Fragment, target.RawFragment = "", ""
	// Hostnames are case-insensitive and explicit default ports identify the
	// same HTTP origin. Preserve the path and query exactly: they may select a
	// different product or variant even when they resemble tracking parameters.
	target.Host = strings.ToLower(target.Host)
	if port := target.Port(); target.Scheme == "https" && port == "443" || target.Scheme == "http" && port == "80" {
		target.Host = strings.TrimSuffix(target.Host, ":"+port)
	}
	return sha256.Sum256([]byte(target.String()))
}

func (c *previewCache) acquire(ctx context.Context, key previewKey) (domain.WishImportPreview, bool, func(), error) {
	for {
		if err := previewContextError(ctx); err != nil {
			return domain.WishImportPreview{}, false, nil, err
		}
		c.mu.Lock()
		c.expire(c.now())
		if err := previewContextError(ctx); err != nil {
			c.mu.Unlock()
			return domain.WishImportPreview{}, false, nil, err
		}
		if entry, ok := c.entries[key]; ok {
			c.order++
			entry.order = c.order
			c.entries[key] = entry
			c.mu.Unlock()
			result := clonePreview(entry.value)
			if entry.image != nil {
				result.Image.ContentBase64 = base64.StdEncoding.EncodeToString(entry.image)
			}
			if err := previewContextError(ctx); err != nil {
				return domain.WishImportPreview{}, false, nil, err
			}
			if !entry.staleAt.IsZero() && !c.now().Before(entry.staleAt) && result.Price != nil {
				result.Warnings = append(result.Warnings, domain.WishImportPriceMayBeStale)
			}
			return result, true, nil, nil
		}
		if done, ok := c.pending[key]; ok {
			c.mu.Unlock()
			select {
			case <-ctx.Done():
				return domain.WishImportPreview{}, false, nil, ctx.Err()
			case <-done:
				continue
			}
		}
		if len(c.pending) >= maximumPendingPreviewKeys {
			c.mu.Unlock()
			return domain.WishImportPreview{}, false, func() {}, nil
		}
		done := make(chan struct{})
		c.pending[key] = done
		c.mu.Unlock()
		return domain.WishImportPreview{}, false, func() {
			c.mu.Lock()
			delete(c.pending, key)
			close(done)
			c.mu.Unlock()
		}, nil
	}
}

func previewContextError(ctx context.Context) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	// The owner's completion and a waiter's deadline may become ready together.
	// Its cancellation callback can run just after the key is released, so also
	// check the deadline itself before admitting another provider operation.
	if deadline, ok := ctx.Deadline(); ok && time.Until(deadline) <= 0 {
		return context.DeadlineExceeded
	}
	return nil
}

func (c *previewCache) put(key previewKey, value domain.WishImportPreview, ambiguousPrice bool, priceFreshUntil time.Time) {
	complete := value.Price != nil && (len(value.Warnings) == 0 || len(value.Warnings) == 1 && value.Warnings[0] == domain.WishImportPriceMayBeStale)
	knownAmbiguity := ambiguousPrice && value.Price == nil && len(value.Warnings) == 1 && value.Warnings[0] == "WISH_IMPORT_PRICE_UNAVAILABLE"
	if value.Name == nil || value.Image == nil || (!complete && !knownAmbiguity) {
		return
	}
	value = clonePreview(value)
	value.Url = nil
	var image []byte
	imageSize := len(value.Image.ContentBase64)
	if c.adaptive {
		var err error
		image, err = base64.StdEncoding.DecodeString(value.Image.ContentBase64)
		if err != nil || len(image) == 0 {
			return
		}
		value.Image.ContentBase64 = ""
		imageSize = len(image)
	}
	size := len(*value.Name) + len(value.Image.ContentType) + imageSize + 128
	for _, detail := range []*string{value.Note, value.Color, value.Size} {
		if detail != nil {
			size += len(*detail)
		}
	}
	if value.Price != nil {
		size += len(*value.Price)
	}
	if size > maximumPreviewCacheBytes {
		return
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	now := c.now()
	c.expire(now)
	expires := now.Add(previewCacheLifetime)
	if c.adaptive && !priceFreshUntil.IsZero() {
		expires = minTime(priceFreshUntil, now.Add(maximumPriceFreshness-providerFreshnessSafety))
	}
	if !expires.After(now) {
		return
	}
	if previous, ok := c.entries[key]; ok {
		c.bytes -= previous.size
		delete(c.entries, key)
	}
	for len(c.entries) >= maximumPreviewCacheEntries || c.bytes+size > maximumPreviewCacheBytes {
		var oldest previewKey
		order := ^uint64(0)
		for candidate, entry := range c.entries {
			if entry.order < order {
				oldest, order = candidate, entry.order
			}
		}
		c.bytes -= c.entries[oldest].size
		delete(c.entries, oldest)
	}
	c.order++
	var staleAt time.Time
	if c.adaptive && value.Price != nil && len(value.Warnings) == 0 && expires.After(now.Add(previewCacheLifetime)) {
		staleAt = now.Add(previewCacheLifetime)
	}
	c.entries[key] = previewEntry{value: value, image: image, expires: expires, staleAt: staleAt, order: c.order, size: size}
	c.bytes += size
}

func minTime(a, b time.Time) time.Time {
	if a.Before(b) {
		return a
	}
	return b
}

func (c *previewCache) expire(now time.Time) {
	for key, entry := range c.entries {
		if !now.Before(entry.expires) {
			c.bytes -= entry.size
			delete(c.entries, key)
		}
	}
}

func clonePreview(value domain.WishImportPreview) domain.WishImportPreview {
	clone := func(source *string) *string {
		if source == nil {
			return nil
		}
		copy := *source
		return &copy
	}
	value.Note, value.Color, value.Size = clone(value.Note), clone(value.Color), clone(value.Size)
	if value.Name != nil {
		name := *value.Name
		value.Name = &name
	}
	if value.Price != nil {
		price := *value.Price
		value.Price = &price
	}
	if value.Image != nil {
		picture := *value.Image
		value.Image = &picture
	}
	value.Warnings = append([]string{}, value.Warnings...)
	return value
}
