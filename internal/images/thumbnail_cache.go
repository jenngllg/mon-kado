package images

import (
	"bytes"
	"container/list"
	"context"
	"crypto/sha256"
	"errors"
	"sync"
)

const thumbnailCacheBytes = 32 * 1024 * 1024
const thumbnailCacheEntries = 1024

type thumbnailValue struct {
	content   []byte
	mediaType string
}

type thumbnailEntry struct {
	key   [sha256.Size]byte
	value thumbnailValue
}

type thumbnailFlight struct {
	done  chan struct{}
	value thumbnailValue
	err   error
}

// ThumbnailCache reuses derived images within a bounded process-local LRU.
// Callers must authorize access to the source before every lookup.
type ThumbnailCache struct {
	mu             sync.Mutex
	entries        map[[sha256.Size]byte]*list.Element
	order          *list.List
	flights        map[[sha256.Size]byte]*thumbnailFlight
	bytes          int
	maximumBytes   int
	maximumEntries int
}

// NewThumbnailCache creates an empty cache limited to 32 MiB and 1024 images.
func NewThumbnailCache() *ThumbnailCache {
	return &ThumbnailCache{
		entries:        make(map[[sha256.Size]byte]*list.Element),
		order:          list.New(),
		flights:        make(map[[sha256.Size]byte]*thumbnailFlight),
		maximumBytes:   thumbnailCacheBytes,
		maximumEntries: thumbnailCacheEntries,
	}
}

// Thumbnail returns an independent copy of a cached or newly generated image.
// Concurrent requests for identical source bytes share one conversion; waiting
// callers retain their own cancellation and never cancel another request.
func (c *ThumbnailCache) Thumbnail(ctx context.Context, source []byte) ([]byte, string, error) {
	if err := ctx.Err(); err != nil {
		return nil, "", err
	}
	key := sha256.Sum256(source)
	for {
		c.mu.Lock()
		if entry := c.entries[key]; entry != nil {
			c.order.MoveToFront(entry)
			value := entry.Value.(thumbnailEntry).value
			c.mu.Unlock()
			return bytes.Clone(value.content), value.mediaType, nil
		}
		if flight := c.flights[key]; flight != nil {
			c.mu.Unlock()
			select {
			case <-ctx.Done():
				return nil, "", ctx.Err()
			case <-flight.done:
				if err := ctx.Err(); err != nil {
					return nil, "", err
				}
				// A disconnected producer must not fail another live request.
				if errors.Is(flight.err, context.Canceled) || errors.Is(flight.err, context.DeadlineExceeded) {
					continue
				}
				return bytes.Clone(flight.value.content), flight.value.mediaType, flight.err
			}
		}
		flight := &thumbnailFlight{done: make(chan struct{})}
		c.flights[key] = flight
		c.mu.Unlock()

		content, mediaType, err := Thumbnail(ctx, source)
		c.mu.Lock()
		flight.value, flight.err = thumbnailValue{content: content, mediaType: mediaType}, err
		if err == nil && len(content) <= c.maximumBytes && c.maximumEntries > 0 {
			entry := c.order.PushFront(thumbnailEntry{key: key, value: flight.value})
			c.entries[key] = entry
			c.bytes += len(content)
			for c.bytes > c.maximumBytes || c.order.Len() > c.maximumEntries {
				oldest := c.order.Back()
				value := oldest.Value.(thumbnailEntry)
				delete(c.entries, value.key)
				c.bytes -= len(value.value.content)
				c.order.Remove(oldest)
			}
		}
		delete(c.flights, key)
		close(flight.done)
		c.mu.Unlock()
		return bytes.Clone(content), mediaType, err
	}
}
