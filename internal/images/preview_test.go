package images_test

import (
	"bytes"
	"context"
	"errors"
	"image"
	"image/color"
	"image/png"
	"testing"

	"github.com/JennGllg/mon-kado/internal/images"
)

func TestComposePreview_WhenNormalizedImagesExist_CentersThemOnIvoryJPEG(t *testing.T) {
	// Arrange
	picture := image.NewNRGBA(image.Rect(0, 0, 40, 20))
	for y := 0; y < 20; y++ {
		for x := 0; x < 40; x++ {
			picture.Set(x, y, color.NRGBA{255, 0, 0, 255})
		}
	}
	var source bytes.Buffer
	if err := png.Encode(&source, picture); err != nil {
		t.Fatal(err)
	}
	normalized, err := images.Process(t.Context(), source.Bytes(), 1600, false)
	if err != nil {
		t.Fatal(err)
	}
	for _, count := range []int{1, 2} {
		t.Run(string(rune('0'+count)), func(t *testing.T) {
			// Act
			contents := [][]byte{normalized.Content}
			if count == 2 {
				contents = append(contents, normalized.Content)
			}
			output, err := images.ComposePreview(t.Context(), contents)
			// Assert
			if err != nil {
				t.Fatal(err)
			}
			decoded, format, err := image.Decode(bytes.NewReader(output))
			if err != nil || format != "jpeg" || decoded.Bounds().Dx() != 1200 || decoded.Bounds().Dy() != 630 {
				t.Fatal("invalid preview image")
			}
			r, g, b, _ := decoded.At(0, 0).RGBA()
			if r>>8 < 248 || g>>8 < 247 || b>>8 < 243 {
				t.Fatal("canvas background differs")
			}
		})
	}
}

func TestComposePreview_WhenInputIsInvalid_RejectsWithoutAllocatingUnboundedImages(t *testing.T) {
	// Arrange
	for _, contents := range [][][]byte{nil, {{}, {}, {}}, {{}}, {{1, 2, 3}}, {make([]byte, images.MaximumInputBytes+1)}} {
		// Act
		_, err := images.ComposePreview(t.Context(), contents)
		// Assert
		if err == nil {
			t.Fatal("invalid preview input accepted")
		}
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	_, err := images.ComposePreview(ctx, [][]byte{{1}})
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("cancellation lost: %v", err)
	}
}
