package images_test

import (
	"bytes"
	"image"
	"image/color"
	"image/draw"
	"image/png"
	"testing"

	"github.com/JennGllg/mon-kado/internal/images"
)

func TestThumbnail_WhenOpaqueOrTransparent_PreservesAspectAndTransparency(t *testing.T) {
	for _, transparent := range []bool{false, true} {
		t.Run(map[bool]string{false: "opaque", true: "transparent"}[transparent], func(t *testing.T) {
			// Arrange
			source := image.NewNRGBA(image.Rect(0, 0, 1200, 800))
			if !transparent {
				draw.Draw(source, source.Bounds(), image.NewUniform(color.White), image.Point{}, draw.Src)
			}
			var encoded bytes.Buffer
			if err := png.Encode(&encoded, source); err != nil {
				t.Fatal(err)
			}
			// Act
			content, mediaType, err := images.Thumbnail(t.Context(), encoded.Bytes())
			// Assert
			if err != nil {
				t.Fatal(err)
			}
			result, format, err := image.Decode(bytes.NewReader(content))
			if err != nil || result.Bounds().Dx() != 600 || result.Bounds().Dy() != 400 {
				t.Fatal("incorrect thumbnail dimensions")
			}
			if transparent {
				_, _, _, alpha := result.At(0, 0).RGBA()
				if format != "png" || mediaType != "image/png" || alpha != 0 {
					t.Fatal("transparency lost")
				}
			} else if format != "jpeg" || mediaType != "image/jpeg" {
				t.Fatal("opaque thumbnail must use JPEG")
			}
		})
	}
}
