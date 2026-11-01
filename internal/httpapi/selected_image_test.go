package httpapi

import (
	"bytes"
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"image"
	"image/png"
	"net/url"
	"testing"
	"time"
)

type selectedDownloaderFake struct {
	content      []byte
	err          error
	calls        int
	contextValue any
	limit        int64
}

type selectedImageContextKey struct{}

func (f *selectedDownloaderFake) DownloadSelectedImage(ctx context.Context, _ string, limit int64) ([]byte, *url.URL, string, error) {
	f.calls++
	f.contextValue = ctx.Value(selectedImageContextKey{})
	f.limit = limit
	return f.content, nil, "image/png", f.err
}

func TestSelectedImage_WhenDownloadIsAvailable_ReturnsBoundedNormalizedContent(t *testing.T) {
	// Arrange
	var buf bytes.Buffer
	if err := png.Encode(&buf, image.NewNRGBA(image.Rect(0, 0, 4, 5))); err != nil {
		t.Fatal(err)
	}
	fake := &selectedDownloaderFake{content: buf.Bytes()}
	client := selectedImageClient{client: fake, permits: make(chan struct{}, 1), timeout: time.Second}
	ctx := context.WithValue(t.Context(), selectedImageContextKey{}, "value")
	// Act
	result, err := client.Resolve(ctx, "https://images.example/item.png")
	// Assert
	if err != nil || result.ContentType != "image/webp" || result.ContentBase64 == "" || fake.calls != 1 || fake.contextValue != "value" || fake.limit != images.MaximumInputBytes {
		t.Fatalf("unexpected result: %#v %v", result, err)
	}
}

func TestSelectedImage_WhenSourceFails_ReturnsSafeError(t *testing.T) {
	// Arrange
	fake := &selectedDownloaderFake{err: errors.New("sensitive provider response")}
	client := selectedImageClient{client: fake, permits: make(chan struct{}, 1), timeout: time.Second}
	// Act
	_, err := client.Resolve(t.Context(), "https://images.example/item.png")
	// Assert
	var apiErr *domain.Error
	if !errors.As(err, &apiErr) || apiErr.Code != domain.WishImageImportUnavailable || fake.calls != 1 || err.Error() == fake.err.Error() {
		t.Fatalf("unexpected error: %v", err)
	}
}

func TestSelectedImage_WhenContextIsCancelled_DoesNotDownload(t *testing.T) {
	// Arrange
	fake := &selectedDownloaderFake{}
	permits := make(chan struct{}, 1)
	permits <- struct{}{}
	client := selectedImageClient{client: fake, permits: permits, timeout: time.Second}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	// Act
	_, err := client.Resolve(ctx, "https://images.example/item.png")
	// Assert
	if !errors.Is(err, context.Canceled) || fake.calls != 0 {
		t.Fatalf("unexpected error: %v", err)
	}
}
