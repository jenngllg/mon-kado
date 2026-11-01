package images

import (
	"bytes"
	"context"
	"errors"
	"image"
	"image/color"
	"image/jpeg"
	"math"

	"golang.org/x/image/draw"
)

// ComposePreview creates a metadata-free 1200 by 630 JPEG from at most two normalized images.
func ComposePreview(ctx context.Context, contents [][]byte) ([]byte, error) {
	if len(contents) < 1 || len(contents) > 2 {
		return nil, errors.New("a preview requires one or two images")
	}
	select {
	case processingSlots <- struct{}{}:
		defer func() { <-processingSlots }()
	case <-ctx.Done():
		return nil, ctx.Err()
	}
	canvas := image.NewRGBA(image.Rect(0, 0, 1200, 630))
	draw.Draw(canvas, canvas.Bounds(), image.NewUniform(color.RGBA{250, 249, 245, 255}), image.Point{}, draw.Src)
	cellWidth := 1200 / len(contents)
	for index, content := range contents {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		if len(content) > MaximumInputBytes {
			return nil, errors.New("preview image exceeds the storage limit")
		}
		cfg, format, err := image.DecodeConfig(bytes.NewReader(content))
		if err != nil || format != "webp" || cfg.Width < 1 || cfg.Height < 1 || cfg.Width > 1600 || cfg.Height > 1600 {
			return nil, errors.New("invalid normalized preview image")
		}
		picture, _, err := image.Decode(bytes.NewReader(content))
		if err != nil {
			return nil, err
		}
		scale := math.Min(float64(cellWidth-48)/float64(cfg.Width), float64(630-48)/float64(cfg.Height))
		width, height := max(1, int(math.Round(float64(cfg.Width)*scale))), max(1, int(math.Round(float64(cfg.Height)*scale)))
		left, top := index*cellWidth+(cellWidth-width)/2, (630-height)/2
		draw.BiLinear.Scale(canvas, image.Rect(left, top, left+width, top+height), picture, picture.Bounds(), draw.Over, nil)
	}
	var output bytes.Buffer
	if err := jpeg.Encode(&output, canvas, &jpeg.Options{Quality: 85}); err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	return output.Bytes(), nil
}
