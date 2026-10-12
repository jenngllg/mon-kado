// Package images validates and normalizes untrusted images without native libraries.
package images

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/binary"
	"errors"
	"image"
	_ "image/jpeg"
	_ "image/png"
	"math"

	"github.com/HugoSmits86/nativewebp"
	"github.com/JennGllg/mon-kado/internal/domain"
	"golang.org/x/image/draw"
	_ "golang.org/x/image/webp"
)

// MaximumInputBytes limits both upload and imported source image sizes.
const MaximumInputBytes = 10 * 1024 * 1024

var processingSlots = make(chan struct{}, 2)

// Result contains a stripped, normalized WebP and its integrity hash.
type Result struct {
	Content []byte
	Hash    []byte
}

// Process rejects animated or oversized images and honors cancellation before each expensive stage.
func Process(ctx context.Context, source []byte, maximumEdge int, profile bool) (Result, error) {
	invalid, unsupported := domain.WishImageInvalid, domain.WishImageUnsupportedFormat
	if profile {
		invalid, unsupported = domain.ProfileImageInvalid, domain.ProfileImageUnsupportedFormat
	}
	fail := func(code, message string) (Result, error) { return Result{}, domain.Failure(400, code, message) }
	if len(source) == 0 || len(source) > MaximumInputBytes {
		return fail(invalid, "The image is empty or exceeds 10 MiB.")
	}
	select {
	case processingSlots <- struct{}{}:
		defer func() { <-processingSlots }()
	case <-ctx.Done():
		return Result{}, ctx.Err()
	}
	configuration, format, err := image.DecodeConfig(bytes.NewReader(source))
	if format != "jpeg" && format != "png" && format != "webp" {
		return fail(unsupported, "Only static JPEG, PNG and WebP images are supported.")
	}
	if err != nil || configuration.Width < 1 || configuration.Height < 1 || int64(configuration.Width)*int64(configuration.Height) > 40000000 {
		return fail(invalid, "The image is corrupt or its dimensions exceed the allowed limit.")
	}
	if animated(source, format) {
		return fail(unsupported, "Animated images are not supported.")
	}
	if err = ctx.Err(); err != nil {
		return Result{}, err
	}
	decoded, _, err := image.Decode(bytes.NewReader(source))
	if err != nil {
		return fail(invalid, "The image cannot be decoded.")
	}
	oriented := orient(decoded, orientation(source, format))
	width, height := oriented.Bounds().Dx(), oriented.Bounds().Dy()
	scale := math.Min(1, float64(maximumEdge)/float64(max(width, height)))
	output := image.NewNRGBA(image.Rect(0, 0, max(1, int(math.Round(float64(width)*scale))), max(1, int(math.Round(float64(height)*scale)))))
	draw.CatmullRom.Scale(output, output.Bounds(), oriented, oriented.Bounds(), draw.Src, nil)
	if err = ctx.Err(); err != nil {
		return Result{}, err
	}
	var buffer bytes.Buffer
	if err = nativewebp.Encode(&buffer, output, nil); err != nil {
		return Result{}, err
	}
	if buffer.Len() > MaximumInputBytes {
		return fail(invalid, "The normalized image exceeds 10 MiB.")
	}
	if err = ctx.Err(); err != nil {
		return Result{}, err
	}
	content := buffer.Bytes()
	hash := sha256.Sum256(content)
	return Result{Content: content, Hash: hash[:]}, nil
}

func animated(data []byte, format string) bool {
	if format == "png" {
		for offset := 8; offset+12 <= len(data); {
			length := int64(binary.BigEndian.Uint32(data[offset : offset+4]))
			if length > int64(len(data)-offset-12) {
				return true
			}
			if string(data[offset+4:offset+8]) == "acTL" {
				return true
			}
			offset += int(length) + 12
		}
	}
	if format == "webp" {
		for offset := 12; offset+8 <= len(data); {
			length := int64(binary.LittleEndian.Uint32(data[offset+4 : offset+8]))
			if length > int64(len(data)-offset-8) {
				return true
			}
			kind := string(data[offset : offset+4])
			if kind == "ANIM" || kind == "ANMF" || kind == "VP8X" && length > 0 && data[offset+8]&2 != 0 {
				return true
			}
			offset += 8 + int(length) + (int(length) & 1)
		}
	}
	return false
}

func orientation(data []byte, format string) int {
	if format == "jpeg" {
		for offset := 2; offset+4 < len(data) && data[offset] == 0xff; {
			marker := data[offset+1]
			if marker == 0xda || marker == 0xd9 {
				break
			}
			length := int(binary.BigEndian.Uint16(data[offset+2 : offset+4]))
			if length < 2 || offset+length+2 > len(data) {
				break
			}
			if marker == 0xe1 {
				if value, err := exifOrientation(data[offset+4 : offset+length+2]); err == nil {
					return value
				}
			}
			offset += length + 2
		}
	}
	if format == "png" || format == "webp" {
		start, overhead := 8, 12
		if format == "webp" {
			start, overhead = 12, 8
		}
		for offset := start; offset+overhead <= len(data); {
			length := int64(binary.BigEndian.Uint32(data[offset : offset+4]))
			kind := string(data[offset+4 : offset+8])
			if format == "webp" {
				kind = string(data[offset : offset+4])
				length = int64(binary.LittleEndian.Uint32(data[offset+4 : offset+8]))
			}
			if length > int64(len(data)-offset-overhead) {
				break
			}
			if kind == "eXIf" || kind == "EXIF" {
				if value, err := exifOrientation(data[offset+8 : offset+8+int(length)]); err == nil {
					return value
				}
			}
			offset += overhead + int(length)
			if format == "webp" {
				offset += int(length) & 1
			}
		}
	}
	return 1
}

func exifOrientation(data []byte) (int, error) {
	if bytes.HasPrefix(data, []byte("Exif\x00\x00")) {
		data = data[6:]
	}
	invalid := errors.New("invalid EXIF orientation")
	if len(data) < 8 {
		return 1, invalid
	}
	var order binary.ByteOrder
	switch string(data[:2]) {
	case "II":
		order = binary.LittleEndian
	case "MM":
		order = binary.BigEndian
	default:
		return 1, invalid
	}
	if order.Uint16(data[2:4]) != 42 {
		return 1, invalid
	}
	offset := uint64(order.Uint32(data[4:8]))
	if offset+2 > uint64(len(data)) {
		return 1, invalid
	}
	count := order.Uint16(data[offset : offset+2])
	for i := uint64(0); i < uint64(count); i++ {
		entry := offset + 2 + i*12
		if entry+12 > uint64(len(data)) {
			return 1, invalid
		}
		if order.Uint16(data[entry:entry+2]) == 0x112 && order.Uint16(data[entry+2:entry+4]) == 3 && order.Uint32(data[entry+4:entry+8]) == 1 {
			value := int(order.Uint16(data[entry+8 : entry+10]))
			if value >= 1 && value <= 8 {
				return value, nil
			}
		}
	}
	return 1, nil
}

func orient(source image.Image, orientation int) image.Image {
	if orientation == 1 {
		return source
	}
	bounds := source.Bounds()
	width, height := bounds.Dx(), bounds.Dy()
	outWidth, outHeight := width, height
	if orientation >= 5 {
		outWidth, outHeight = height, width
	}
	output := image.NewNRGBA(image.Rect(0, 0, outWidth, outHeight))
	for y := 0; y < height; y++ {
		for x := 0; x < width; x++ {
			dx, dy := x, y
			switch orientation {
			case 2:
				dx = width - 1 - x
			case 3:
				dx, dy = width-1-x, height-1-y
			case 4:
				dy = height - 1 - y
			case 5:
				dx, dy = y, x
			case 6:
				dx, dy = height-1-y, x
			case 7:
				dx, dy = height-1-y, width-1-x
			case 8:
				dx, dy = y, width-1-x
			}
			output.Set(dx, dy, source.At(bounds.Min.X+x, bounds.Min.Y+y))
		}
	}
	return output
}
