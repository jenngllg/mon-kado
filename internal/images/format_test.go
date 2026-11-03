package images_test

import (
	"bytes"
	"image"
	"image/color"
	"image/jpeg"
	"os"
	"path/filepath"
	"testing"

	"github.com/JennGllg/mon-kado/internal/images"
)

func TestProcess_WhenPhotographIsOpaque_EncodesBoundedJPEGWithoutMetadata(t *testing.T) {
	// Arrange
	source := image.NewNRGBA(image.Rect(0, 0, 64, 32))
	for y := range 32 {
		for x := range 64 {
			source.SetNRGBA(x, y, color.NRGBA{R: uint8(x * 3), G: uint8(y * 7), A: 255})
		}
	}
	var input bytes.Buffer
	if err := jpeg.Encode(&input, source, &jpeg.Options{Quality: 95}); err != nil {
		t.Fatal(err)
	}
	// Act
	result, err := images.ProcessForStorage(t.Context(), input.Bytes(), 32, false, true)
	// Assert
	if err != nil {
		t.Fatal(err)
	}
	config, format, err := image.DecodeConfig(bytes.NewReader(result.Content))
	if err != nil || format != "jpeg" || config.Width != 32 || config.Height != 16 || result.ContentType != "image/jpeg" || images.Extension(result.Content) != ".jpg" {
		t.Fatal("photo format or dimensions differ")
	}
}

func TestProcessWebP_WhenLegacyConsumerRequiresWebP_PreservesContract(t *testing.T) {
	// Arrange
	var input bytes.Buffer
	if err := jpeg.Encode(&input, image.NewGray(image.Rect(0, 0, 8, 6)), nil); err != nil {
		t.Fatal(err)
	}
	// Act
	result, err := images.Process(t.Context(), input.Bytes(), 4, false)
	// Assert
	if err != nil || result.ContentType != "image/webp" || images.MediaType(result.Content) != "image/webp" || images.Extension(result.Content) != ".webp" {
		t.Fatal("legacy WebP contract changed")
	}
}

func TestStorage_WhenLegacyWebPExists_ReadsAndRemovesAlongsideCurrentStorage(t *testing.T) {
	// Arrange
	root, id := t.TempDir(), "0198d027-51c0-7000-8000-000000000007"
	legacy := []byte("legacy immutable fixture")
	if err := os.WriteFile(filepath.Join(root, id+".webp"), legacy, 0600); err != nil {
		t.Fatal(err)
	}
	// Act
	read, err := images.Read(t.Context(), root, id)
	// Assert
	if err != nil || !bytes.Equal(read, legacy) {
		t.Fatal("legacy storage became unreadable")
	}
	if err = images.Remove(root, id); err != nil {
		t.Fatal(err)
	}
	if _, err = images.Read(t.Context(), root, id); !os.IsNotExist(err) {
		t.Fatal("legacy file remains")
	}
}

func TestExtension_WhenContentIsUnsupported_ReturnsNoImageSuffix(t *testing.T) {
	// Arrange / Act / Assert
	if images.Extension([]byte("unsupported")) != "" {
		t.Fatal("unsupported content accepted")
	}
}
