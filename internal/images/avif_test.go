package images_test

import (
	"bytes"
	"context"
	"encoding/binary"
	"errors"
	"image"
	"image/color"
	"os"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/gen2brain/gav1d/avif"
)

func avifFixture(t *testing.T, name string) []byte {
	t.Helper()
	b, err := os.ReadFile("testdata/avif/" + name + ".avif")
	if err != nil {
		t.Fatal(err)
	}
	return b
}

func TestProcessImported_WhenAVIFIsStatic_NormalizesOrientedPixelsAndAlphaToWebP(t *testing.T) {
	for _, name := range []string{"yuv420-8", "alpha-420", "grid-2x2", "irot1"} {
		t.Run(name, func(t *testing.T) {
			// Arrange
			source := avifFixture(t, name)
			expected, err := avif.Decode(bytes.NewReader(source), avif.Options{AutoRotate: true})
			if err != nil {
				t.Fatal(err)
			}
			// Act
			result, err := images.ProcessImported(t.Context(), source, 1600)
			// Assert
			if err != nil {
				t.Fatal(err)
			}
			decoded, format, err := image.Decode(bytes.NewReader(result.Content))
			if err != nil || format != "webp" || decoded.Bounds() != expected.Bounds() || bytes.Contains(result.Content, []byte("EXIF")) {
				t.Fatal("static AVIF dimensions, orientation or stripping changed")
			}
			for y := expected.Bounds().Min.Y; y < expected.Bounds().Max.Y; y++ {
				for x := expected.Bounds().Min.X; x < expected.Bounds().Max.X; x++ {
					r, g, b, a := color.NRGBAModel.Convert(expected.At(x, y)).RGBA()
					r2, g2, b2, a2 := decoded.At(x, y).RGBA()
					// Unpremultiplying into the normalized 8-bit image can round one level.
					close := func(a, b uint32) bool { return int64(a)-int64(b) <= 257 && int64(b)-int64(a) <= 257 }
					if !close(r, r2) || !close(g, g2) || !close(b, b2) || a != a2 {
						t.Fatalf("normalized pixel differs at %d,%d: %d,%d,%d,%d -> %d,%d,%d,%d", x, y, r, g, b, a, r2, g2, b2, a2)
					}
				}
			}
		})
	}
}

func TestProcessImported_WhenAVIFIsAnimatedCorruptOrOversized_RejectsBeforePixelDecoding(t *testing.T) {
	for _, tc := range []struct{ name, code string }{
		{"animated", domain.WishImageUnsupportedFormat}, {"truncated", domain.WishImageInvalid},
		{"oversized", domain.WishImageInvalid}, {"aggregate area", domain.WishImageInvalid},
		{"box limit", domain.WishImageInvalid}, {"trailing bytes", domain.WishImageInvalid},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			source := avifFixture(t, "yuv420-8")
			switch tc.name {
			case "animated":
				source = avifFixture(t, "anim")
			case "truncated":
				source = source[:len(source)-10]
			case "oversized", "aggregate area":
				if tc.name == "aggregate area" {
					source = avifFixture(t, "alpha-420")
				}
				i := bytes.Index(source, []byte("ispe"))
				binary.BigEndian.PutUint32(source[i+8:], 5000)
				binary.BigEndian.PutUint32(source[i+12:], 5000)
				if tc.name == "aggregate area" {
					binary.BigEndian.PutUint32(source[i+12:], 2000)
				}
			case "box limit":
				for range 513 {
					source = append(source, 0, 0, 0, 8, 'f', 'r', 'e', 'e')
				}
			case "trailing bytes":
				source = append(source, 1)
			}
			// Act
			_, err := images.ProcessImported(t.Context(), source, 1600)
			// Assert
			var failure *domain.Error
			if !errors.As(err, &failure) || failure.Code != tc.code {
				t.Fatalf("untrusted AVIF error = %v", err)
			}
		})
	}
}

func TestProcess_WhenAVIFIsUploaded_KeepsExistingUploadContract(t *testing.T) {
	// Arrange
	source := avifFixture(t, "yuv420-8")
	// Act
	_, err := images.Process(t.Context(), source, 1600, false)
	// Assert
	var failure *domain.Error
	if !errors.As(err, &failure) || failure.Code != domain.WishImageUnsupportedFormat {
		t.Fatalf("AVIF upload error = %v", err)
	}
}

func TestProcessImported_WhenContextIsCancelled_DoesNotDecodeAVIF(t *testing.T) {
	// Arrange
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	// Act
	_, err := images.ProcessImported(ctx, avifFixture(t, "yuv420-8"), 1600)
	// Assert
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("AVIF cancellation error = %v", err)
	}
}
