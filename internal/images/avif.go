package images

import (
	"bytes"
	"encoding/binary"
	"image"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/gen2brain/gav1d/avif"
)

// AVIF can contain several independently decoded tiles and an alpha plane. Bound
// their combined declared area as well as each bitstream, before pixel decoding.
const maximumAVIFPixels = 16000000

func validateAVIF(data []byte) error {
	invalid := domain.Failure(400, domain.WishImageInvalid, "The AVIF image is corrupt or exceeds the allowed limit.")
	unsupported := domain.Failure(400, domain.WishImageUnsupportedFormat, "AVIF image sequences are not supported.")
	boxes, items, largestArea := 0, uint64(0), uint64(0)
	var walk func([]byte, int) error
	walk = func(data []byte, depth int) error {
		if depth > 8 {
			return invalid
		}
		for len(data) > 0 {
			boxes++
			if len(data) < 8 || boxes > 512 {
				return invalid
			}
			size, header := uint64(binary.BigEndian.Uint32(data)), uint64(8)
			kind := string(data[4:8])
			if size == 1 {
				if len(data) < 16 {
					return invalid
				}
				size, header = binary.BigEndian.Uint64(data[8:]), 16
			} else if size == 0 {
				size = uint64(len(data))
			}
			if size < header || size > uint64(len(data)) {
				return invalid
			}
			payload := data[header:size]
			switch kind {
			case "ftyp":
				if len(payload) < 8 || len(payload)%4 != 0 {
					return invalid
				}
				for i := 0; i < len(payload); i += 4 {
					if i != 4 && (string(payload[i:i+4]) == "avis" || string(payload[i:i+4]) == "msf1") {
						return unsupported
					}
				}
			case "moov":
				return unsupported
			case "meta":
				if len(payload) < 4 {
					return invalid
				}
				if err := walk(payload[4:], depth+1); err != nil {
					return err
				}
			case "iprp", "ipco":
				if err := walk(payload, depth+1); err != nil {
					return err
				}
			case "iinf":
				if len(payload) < 6 {
					return invalid
				}
				count, start := uint64(binary.BigEndian.Uint16(payload[4:])), 6
				if payload[0] != 0 {
					if len(payload) < 8 {
						return invalid
					}
					count = uint64(binary.BigEndian.Uint32(payload[4:]))
					start = 8
				}
				if count > 128 {
					return invalid
				}
				before := items
				if err := walk(payload[start:], depth+1); err != nil {
					return err
				}
				if items-before != count {
					return invalid
				}
			case "infe":
				items++
				if items > 128 {
					return invalid
				}
			case "ispe":
				if len(payload) != 12 {
					return invalid
				}
				w, h := uint64(binary.BigEndian.Uint32(payload[4:])), uint64(binary.BigEndian.Uint32(payload[8:]))
				largestArea = max(largestArea, w*h)
				if w == 0 || h == 0 || largestArea > maximumAVIFPixels {
					return invalid
				}
			}
			data = data[size:]
		}
		return nil
	}
	if err := walk(data, 0); err != nil {
		return err
	}
	// Properties can be shared by many items. Multiplying by the item count
	// conservatively covers every tile and alpha plane rather than counting
	// a shared ispe only once.
	if items == 0 || largestArea == 0 || largestArea*items > maximumAVIFPixels {
		return invalid
	}
	return nil
}

func decodeAVIF(data []byte) (image.Image, error) {
	return avif.Decode(bytes.NewReader(data), avif.Options{AutoRotate: true, FrameSizeLimit: maximumAVIFPixels})
}
