package images_test

import (
	"bytes"
	"encoding/binary"
	"errors"
	"fmt"
	"hash/crc32"
	"image"
	"image/color"
	"image/jpeg"
	"image/png"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
)

func pngWithChunk(t *testing.T, kind string, payload []byte) []byte {
	t.Helper()
	source := image.NewNRGBA(image.Rect(0, 0, 2, 3))
	for y := 0; y < 3; y++ {
		for x := 0; x < 2; x++ {
			source.Set(x, y, color.NRGBA{R: uint8(10 + 30*(y*2+x)), A: 255})
		}
	}
	var encoded bytes.Buffer
	if err := png.Encode(&encoded, source); err != nil {
		t.Fatal(err)
	}
	var chunk bytes.Buffer
	binary.Write(&chunk, binary.BigEndian, uint32(len(payload)))
	chunk.WriteString(kind)
	chunk.Write(payload)
	binary.Write(&chunk, binary.BigEndian, crc32.ChecksumIEEE(append([]byte(kind), payload...)))
	data := encoded.Bytes()
	return append(append(append([]byte{}, data[:33]...), chunk.Bytes()...), data[33:]...)
}

func orientationTIFF(value uint16, order binary.ByteOrder) []byte {
	data := make([]byte, 26)
	copy(data, "II")
	if order == binary.BigEndian {
		copy(data, "MM")
	}
	order.PutUint16(data[2:], 42)
	order.PutUint32(data[4:], 8)
	order.PutUint16(data[8:], 1)
	order.PutUint16(data[10:], 0x112)
	order.PutUint16(data[12:], 3)
	order.PutUint32(data[14:], 1)
	order.PutUint16(data[18:], value)
	return data
}

func TestProcess_WhenExifOrientationPresent_RotatesPixelsAndStripsMetadata(t *testing.T) {
	for _, order := range []binary.ByteOrder{binary.LittleEndian, binary.BigEndian} {
		for _, tc := range []struct {
			orientation         uint16
			width, height, x, y int
		}{{1, 2, 3, 0, 0}, {2, 2, 3, 1, 0}, {3, 2, 3, 1, 2}, {4, 2, 3, 0, 2}, {5, 3, 2, 0, 0}, {6, 3, 2, 2, 0}, {7, 3, 2, 2, 1}, {8, 3, 2, 0, 1}} {
			t.Run(fmt.Sprintf("%s-%d", order.String(), tc.orientation), func(t *testing.T) {
				// Arrange
				source := pngWithChunk(t, "eXIf", orientationTIFF(tc.orientation, order))
				// Act
				result, err := images.Process(t.Context(), source, 1600, false)
				if err != nil {
					t.Fatal(err)
				}
				decoded, _, err := image.Decode(bytes.NewReader(result.Content))
				// Assert
				if err != nil || decoded.Bounds().Dx() != tc.width || decoded.Bounds().Dy() != tc.height {
					t.Fatal("orientation dimensions differ")
				}
				red, _, _, _ := decoded.At(tc.x, tc.y).RGBA()
				if red != 10*257 || bytes.Contains(result.Content, []byte("EXIF")) || bytes.Contains(result.Content, []byte("eXIf")) {
					t.Fatal("orientation pixel mapping or metadata stripping differs")
				}
			})
		}
	}
}

func TestProcess_WhenExifMalformed_IgnoresUntrustedMetadataWithoutChangingOrientation(t *testing.T) {
	for _, mode := range []string{"short", "byteOrder", "magic", "offset", "truncatedEntry", "wrongTag", "wrongType", "wrongCount", "outOfRange"} {
		t.Run(mode, func(t *testing.T) {
			// Arrange
			metadata := orientationTIFF(6, binary.LittleEndian)
			switch mode {
			case "short":
				metadata = []byte{1}
			case "byteOrder":
				metadata[0] = 'X'
			case "magic":
				metadata[2] = 0
			case "offset":
				binary.LittleEndian.PutUint32(metadata[4:], 1000)
			case "truncatedEntry":
				metadata = metadata[:12]
			case "wrongTag":
				metadata[10] = 0
			case "wrongType":
				metadata[12] = 0
			case "wrongCount":
				metadata[14] = 2
			case "outOfRange":
				metadata[18] = 9
			}
			source := pngWithChunk(t, "eXIf", metadata)
			// Act
			result, err := images.Process(t.Context(), source, 1600, false)
			if err != nil {
				t.Fatal(err)
			}
			decoded, _, err := image.Decode(bytes.NewReader(result.Content))
			// Assert
			if err != nil || decoded.Bounds().Dx() != 2 || decoded.Bounds().Dy() != 3 {
				t.Fatal("invalid EXIF changed image orientation")
			}
		})
	}
}

func TestProcess_WhenJPEGContainsExif_RotatesAndStripsApplicationMetadata(t *testing.T) {
	// Arrange
	var source bytes.Buffer
	if err := jpeg.Encode(&source, image.NewNRGBA(image.Rect(0, 0, 2, 3)), nil); err != nil {
		t.Fatal(err)
	}
	metadata := append([]byte("Exif\x00\x00"), orientationTIFF(6, binary.LittleEndian)...)
	data := source.Bytes()
	payload := append([]byte{0xff, 0xd8, 0xff, 0xe1, 0, byte(len(metadata) + 2)}, metadata...)
	payload = append(payload, data[2:]...)
	// Act
	result, err := images.Process(t.Context(), payload, 512, true)
	if err != nil {
		t.Fatal(err)
	}
	decoded, _, err := image.Decode(bytes.NewReader(result.Content))
	// Assert
	if err != nil || decoded.Bounds().Dx() != 3 || decoded.Bounds().Dy() != 2 || bytes.Contains(result.Content, []byte("Exif")) {
		t.Fatal("JPEG EXIF normalization differs")
	}
}

func TestProcess_WhenAnimatedOrOversized_RejectsWithDomainSpecificError(t *testing.T) {
	for _, tc := range []struct {
		name    string
		content []byte
		profile bool
		code    string
	}{
		{"empty", nil, false, domain.WishImageInvalid},
		{"tooLarge", make([]byte, images.MaximumInputBytes+1), true, domain.ProfileImageInvalid},
		{"unsupported", []byte("GIF89a"), false, domain.WishImageUnsupportedFormat},
		{"APNG", pngWithChunk(t, "acTL", make([]byte, 8)), true, domain.ProfileImageUnsupportedFormat},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			// Act
			_, err := images.Process(t.Context(), tc.content, 512, tc.profile)
			// Assert
			var failure *domain.Error
			if !errors.As(err, &failure) || failure.Code != tc.code {
				t.Fatalf("image rejection differs: %v", err)
			}
		})
	}
}
