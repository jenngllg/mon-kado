package images

import (
	"bytes"
	"context"
	"crypto/sha256"
	"errors"
	"image"
	"image/color"
	"image/png"
	"sync"
	"testing"
)

func cacheSource(t *testing.T, seed uint8) []byte {
	t.Helper()
	picture := image.NewNRGBA(image.Rect(0, 0, 32, 24))
	picture.SetNRGBA(0, 0, color.NRGBA{R: seed, A: 255})
	var buffer bytes.Buffer
	if err := png.Encode(&buffer, picture); err != nil {
		t.Fatal(err)
	}
	return buffer.Bytes()
}

func TestThumbnailCache_WhenSourceRepeatsOrChanges_ReturnsIndependentCorrectImages(t *testing.T) {
	// Arrange
	cache := NewThumbnailCache()
	source := cacheSource(t, 30)
	// Act
	first, mediaType, err := cache.Thumbnail(t.Context(), source)
	if err != nil {
		t.Fatal(err)
	}
	expected := bytes.Clone(first)
	first[0] ^= 255
	second, secondType, err := cache.Thumbnail(t.Context(), source)
	// Assert
	if err != nil || mediaType != secondType || !bytes.Equal(expected, second) || cache.order.Len() != 1 {
		t.Fatal("cached image changed or source was not reused")
	}
	third, _, err := cache.Thumbnail(t.Context(), cacheSource(t, 200))
	if err != nil || bytes.Equal(second, third) || cache.order.Len() != 2 {
		t.Fatal("changed source reused obsolete image")
	}
}

func TestThumbnailCache_WhenCapacityExceeded_EvictsLeastRecentlyUsedImage(t *testing.T) {
	for _, limit := range []string{"entries", "bytes", "oversized"} {
		t.Run(limit, func(t *testing.T) {
			// Arrange
			cache := NewThumbnailCache()
			first, second, third := cacheSource(t, 1), cacheSource(t, 2), cacheSource(t, 3)
			one, _, err := cache.Thumbnail(t.Context(), first)
			if err != nil {
				t.Fatal(err)
			}
			if limit == "entries" {
				cache.maximumEntries = 2
			}
			if limit == "bytes" {
				cache.maximumBytes = len(one) * 2
			}
			if limit == "oversized" {
				cache.maximumBytes = 1
			}
			// Act
			for _, source := range [][]byte{second, first, third} {
				if _, _, err := cache.Thumbnail(t.Context(), source); err != nil {
					t.Fatal(err)
				}
			}
			// Assert
			if limit == "oversized" {
				if cache.entries[sha256.Sum256(third)] != nil {
					t.Fatal("oversized image cached")
				}
				return
			}
			if cache.entries[sha256.Sum256(second)] != nil || cache.entries[sha256.Sum256(third)] == nil || cache.order.Len() > 2 || cache.bytes > cache.maximumBytes {
				t.Fatal("cache limits or LRU order not respected")
			}
		})
	}
}

func TestThumbnailCache_WhenInvalidOrCancelled_DoesNotCacheFailure(t *testing.T) {
	// Arrange
	cache := NewThumbnailCache()
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	// Act / Assert
	if _, _, err := cache.Thumbnail(ctx, cacheSource(t, 1)); !errors.Is(err, context.Canceled) {
		t.Fatal("cancellation lost")
	}
	if _, _, err := cache.Thumbnail(t.Context(), []byte("invalid")); err == nil {
		t.Fatal("invalid source accepted")
	}
	if cache.order.Len() != 0 || len(cache.flights) != 0 || cache.bytes != 0 {
		t.Fatal("failed generation retained")
	}
}

type signalledContext struct {
	context.Context
	ready chan struct{}
	once  sync.Once
}

func (c *signalledContext) Done() <-chan struct{} {
	c.once.Do(func() { close(c.ready) })
	return c.Context.Done()
}

func TestThumbnailCache_WhenWaitingCallerCancelled_LeavesSharedConversionAvailable(t *testing.T) {
	// Arrange
	cache := NewThumbnailCache()
	source := cacheSource(t, 1)
	flight := &thumbnailFlight{done: make(chan struct{})}
	cache.flights[sha256.Sum256(source)] = flight
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	signalled := &signalledContext{Context: ctx, ready: make(chan struct{})}
	completed := make(chan error, 1)
	// Act
	go func() { _, _, err := cache.Thumbnail(signalled, source); completed <- err }()
	<-signalled.ready
	cancel()
	// Assert
	if err := <-completed; !errors.Is(err, context.Canceled) {
		t.Fatal("waiting caller ignored cancellation")
	}
	if cache.flights[sha256.Sum256(source)] != flight {
		t.Fatal("waiting caller removed another conversion")
	}
	flight.value = thumbnailValue{content: []byte("completed"), mediaType: "image/png"}
	close(flight.done)
	content, mediaType, err := cache.Thumbnail(t.Context(), source)
	if err != nil || string(content) != "completed" || mediaType != "image/png" {
		t.Fatal("shared result unavailable")
	}
}

func TestThumbnailCache_WhenConcurrentRequests_ReturnsConsistentIndependentResults(t *testing.T) {
	// Arrange
	cache := NewThumbnailCache()
	source := cacheSource(t, 10)
	const requests = 12
	start := make(chan struct{})
	completed := make(chan error, requests)
	// Act
	for range requests {
		go func() {
			<-start
			content, _, err := cache.Thumbnail(t.Context(), source)
			if err == nil {
				_, err = png.Decode(bytes.NewReader(content))
			}
			completed <- err
		}()
	}
	close(start)
	// Assert
	for range requests {
		if err := <-completed; err != nil {
			t.Fatal(err)
		}
	}
	if cache.order.Len() != 1 || len(cache.flights) != 0 {
		t.Fatal("concurrent conversion retained duplicate entries")
	}
}

func TestThumbnailCache_WhenProducerCancelled_LiveWaiterGeneratesItsOwnImage(t *testing.T) {
	// Arrange
	cache := NewThumbnailCache()
	source := cacheSource(t, 50)
	key := sha256.Sum256(source)
	flight := &thumbnailFlight{done: make(chan struct{})}
	cache.flights[key] = flight
	ctx := &signalledContext{Context: t.Context(), ready: make(chan struct{})}
	completed := make(chan error, 1)
	// Act
	go func() { _, _, err := cache.Thumbnail(ctx, source); completed <- err }()
	<-ctx.ready
	cache.mu.Lock()
	delete(cache.flights, key)
	flight.err = context.Canceled
	close(flight.done)
	cache.mu.Unlock()
	// Assert
	if err := <-completed; err != nil {
		t.Fatal("producer cancellation failed live waiter", err)
	}
	if cache.order.Len() != 1 || len(cache.flights) != 0 {
		t.Fatal("live waiter did not cache its conversion")
	}
}
