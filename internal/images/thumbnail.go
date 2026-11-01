package images

import (
	"bytes"
	"context"
	"image"
	"image/jpeg"
	"image/png"
	"math"

	"golang.org/x/image/draw"
)

const thumbnailEdge = 600
const thumbnailJPEGQuality = 80

// Thumbnail creates a bounded gallery image while preserving transparent pixels.
func Thumbnail(ctx context.Context, source []byte) ([]byte, string, error) {
	select {
	case processingSlots <- struct{}{}:
		defer func() { <-processingSlots }()
	case <-ctx.Done():
		return nil, "", ctx.Err()
	}
	decoded, _, err := image.Decode(bytes.NewReader(source))
	if err != nil {
		return nil, "", err
	}
	if err = ctx.Err(); err != nil {
		return nil, "", err
	}
	width, height := decoded.Bounds().Dx(), decoded.Bounds().Dy()
	scale := math.Min(1, float64(thumbnailEdge)/float64(max(width, height)))
	result := image.NewNRGBA(image.Rect(0, 0, max(1, int(math.Round(float64(width)*scale))), max(1, int(math.Round(float64(height)*scale)))))
	draw.CatmullRom.Scale(result, result.Bounds(), decoded, decoded.Bounds(), draw.Src, nil)
	if err = ctx.Err(); err != nil {
		return nil, "", err
	}
	var buffer bytes.Buffer
	mediaType := "image/jpeg"
	if result.Opaque() {
		err = jpeg.Encode(&buffer, result, &jpeg.Options{Quality: thumbnailJPEGQuality})
	} else {
		mediaType = "image/png"
		err = png.Encode(&buffer, result)
	}
	return buffer.Bytes(), mediaType, err
}
