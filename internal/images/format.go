package images

import "net/http"

// MediaType identifies sanitized current images and legacy WebP files.
func MediaType(content []byte) string {
	return http.DetectContentType(content)
}

// Extension returns the matching export suffix for a supported image.
func Extension(content []byte) string {
	switch MediaType(content) {
	case "image/jpeg":
		return ".jpg"
	case "image/png":
		return ".png"
	case "image/webp":
		return ".webp"
	default:
		return ""
	}
}
