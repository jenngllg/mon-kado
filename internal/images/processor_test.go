package images_test

import (
	"bytes"
	"context"
	"crypto/sha256"
	"errors"
	"github.com/JennGllg/mon-kado/internal/images"
	_ "golang.org/x/image/webp"
	"image"
	"image/color"
	"image/png"
	"testing"
)

func TestProcess_WhenImageExceedsEdge_NormalizesWebPAndIntegrityHash(t *testing.T) {
	// Arrange
	source := image.NewNRGBA(image.Rect(0, 0, 128, 64))
	source.Set(0, 0, color.NRGBA{255, 0, 0, 255})
	var buffer bytes.Buffer
	if err := png.Encode(&buffer, source); err != nil {
		t.Fatal(err)
	}
	// Act
	result, err := images.Process(t.Context(), buffer.Bytes(), 32, false)
	// Assert
	if err != nil {
		t.Fatal(err)
	}
	config, format, err := image.DecodeConfig(bytes.NewReader(result.Content))
	hash := sha256.Sum256(result.Content)
	if err != nil || format != "webp" || config.Width != 32 || config.Height != 16 || !bytes.Equal(result.Hash, hash[:]) {
		t.Fatal("normalization invariant failed")
	}
}

func TestProcess_WhenContextIsCancelled_AbortsProcessing(t *testing.T) {
	// Arrange
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	var buffer bytes.Buffer
	png.Encode(&buffer, image.NewNRGBA(image.Rect(0, 0, 1, 1)))
	// Act
	_, err := images.Process(ctx, buffer.Bytes(), 512, true)
	// Assert
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("got %v", err)
	}
}

func TestStorage_WhenWritingPendingImage_RequiresFinalizeAndSupportsCleanup(t *testing.T) {
	// Arrange
	root, id := t.TempDir(), "0198d027-51c0-7000-8000-000000000007"
	content := []byte("normalized fixture")
	// Act
	err := images.WritePending(t.Context(), root, id, content)
	if err != nil {
		t.Fatal(err)
	}
	read, err := images.Read(t.Context(), root, id)
	images.Finalize(root, id)
	images.Remove(root, id)
	// Assert
	if err != nil || !bytes.Equal(read, content) {
		t.Fatal("stored content changed")
	}
	if _, err = images.Read(t.Context(), root, id); err == nil {
		t.Fatal("removed content remains accessible")
	}
}
