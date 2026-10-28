package importer

import (
	"context"
	"crypto/sha256"
	"net/url"
	"sync"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

const previewCacheLifetime = 2 * time.Minute
const maximumPreviewCacheEntries = 128
const maximumPreviewCacheBytes = 16 * 1024 * 1024
const maximumPendingPreviewKeys = 64

type previewKey [sha256.Size]byte

type previewEntry struct {
	value   domain.WishImportPreview
	expires time.Time
	order   uint64
	size    int
}

type previewCache struct {
	mu      sync.Mutex
	now     func() time.Time
	entries map[previewKey]previewEntry
	pending map[previewKey]chan struct{}
	bytes   int
	order   uint64
}

// NewWithPreviewCache reuses complete public-product previews for two minutes.
// The bounded process-local cache stores no failures or request URL strings.
// Every caller still validates public DNS and keeps its own cancellation budget.
func NewWithPreviewCache(now func() time.Time) *Client {
	if now == nil {
		panic("preview cache clock is required")
	}
	client := New()
	client.cache = &previewCache{now: now, entries: make(map[previewKey]previewEntry), pending: make(map[previewKey]chan struct{})}
	return client
}

func cacheKey(value string) previewKey {
	target, _ := url.Parse(value) // The caller has already validated the URL.
	target.Fragment, target.RawFragment = "", ""
	return sha256.Sum256([]byte(target.String()))
}

func (c *previewCache) acquire(ctx context.Context, key previewKey) (domain.WishImportPreview, bool, func(), error) {
	for {
		if err := ctx.Err(); err != nil {
			return domain.WishImportPreview{}, false, nil, err
		}
		c.mu.Lock()
		c.expire(c.now())
		if err := ctx.Err(); err != nil {
			c.mu.Unlock()
			return domain.WishImportPreview{}, false, nil, err
		}
		if entry, ok := c.entries[key]; ok {
			c.order++
			entry.order = c.order
			c.entries[key] = entry
			c.mu.Unlock()
			return clonePreview(entry.value), true, nil, nil
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

func (c *previewCache) put(key previewKey, value domain.WishImportPreview) {
	if value.Name == nil || value.Price == nil || value.Image == nil || len(value.Warnings) != 0 {
		return
	}
	value = clonePreview(value)
	value.Url = nil
	size := len(*value.Name) + len(*value.Price) + len(value.Image.ContentType) + len(value.Image.ContentBase64) + 128
	if size > maximumPreviewCacheBytes {
		return
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	now := c.now()
	c.expire(now)
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
	c.entries[key] = previewEntry{value: value, expires: now.Add(previewCacheLifetime), order: c.order, size: size}
	c.bytes += size
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
