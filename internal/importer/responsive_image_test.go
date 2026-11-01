package importer_test

import (
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenResponsiveImageURLsContainCommas_PreservesTheWholeURL(t *testing.T) {
	for _, tc := range []struct{ name, srcset, want string }{
		{"commas inside URLs", `https://cdn.test/width=320,height=320/blue.jpg 320w, https://cdn.test/width=1024,height=1024/blue.jpg 1024w`, "https://cdn.test/width=1024,height=1024/blue.jpg"},
		{"no space after separator", `https://cdn.test/width=320,height=320/blue.jpg 320w,https://cdn.test/width=1024,height=1024/blue.jpg 1024w`, "https://cdn.test/width=1024,height=1024/blue.jpg"},
		{"large image is still available", `https://cdn.test/blue.jpg 2400w`, "https://cdn.test/blue.jpg"},
		{"prefer bounded resolution", `https://cdn.test/big.jpg 2400w,https://cdn.test/normal.jpg 1200w`, "https://cdn.test/normal.jpg"},
		{"prefer smallest oversized image", `https://cdn.test/huge.jpg 4000w,https://cdn.test/large.jpg 2400w`, "https://cdn.test/large.jpg"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product")
			document := `<title>Blue notebook</title><img alt="Blue notebook" srcset="` + tc.srcset + `">`
			// Act
			result := importer.Extract([]byte(document), base)
			// Assert
			if result.Image == nil || *result.Image != tc.want {
				t.Fatalf("responsive image URL was not preserved: %v", result.Image)
			}
		})
	}
}

func TestExtract_WhenPreloadedImagesIdentifySelectedVariant_UsesUniqueResponsiveImage(t *testing.T) {
	const preload = `<link rel="preload" as="image" imagesrcset="https://cdn.test/width=320,height=320/photo-12345_1.jpg 320w, https://cdn.test/width=1024,height=1024/photo-12345_1.jpg 1024w">`
	for _, tc := range []struct {
		name, query, markup, want string
	}{
		{"selected variant", "?variant=12345", preload, "https://cdn.test/width=1024,height=1024/photo-12345_1.jpg"},
		{"duplicate identical preload", "?variant=12345", preload + preload, "https://cdn.test/width=1024,height=1024/photo-12345_1.jpg"},
		{"invalid metadata placeholder", "?variant=12345", `<meta property="og:image" content="data:image/gif;base64,R0lGODlh">` + preload, "https://cdn.test/width=1024,height=1024/photo-12345_1.jpg"},
		{"usable metadata takes priority", "?variant=12345", `<meta property="og:image" content="https://cdn.test/product.jpg">` + preload, "https://cdn.test/product.jpg"},
		{"placeholder does not identify variant", "?variant=12346", `<meta property="og:image" content="data:image/gif;base64,R0lGODlh">` + preload, ""},
		{"tracking and variant", "?variant=12345&utm_source=share", preload, "https://cdn.test/width=1024,height=1024/photo-12345_1.jpg"},
		{"unselected variant", "", preload, ""},
		{"different variant", "?variant=12346", preload, ""},
		{"identifier substring", "?variant=1234", preload, ""},
		{"unknown additional selection", "?variant=12345&size=large", preload, ""},
		{"multiple different images", "?variant=12345", preload + strings.ReplaceAll(preload, "photo-12345_1", "other-12345_2"), ""},
		{"non-image preload", "?variant=12345", strings.Replace(preload, `as="image"`, `as="script"`, 1), ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			base, _ := url.Parse("https://merchant.test/product" + tc.query)
			// Act
			result := importer.Extract([]byte(`<title>Vase</title>`+tc.markup), base)
			// Assert
			got := ""
			if result.Image != nil {
				got = *result.Image
			}
			if got != tc.want {
				t.Fatalf("preloaded image %q; want %q", got, tc.want)
			}
		})
	}
}
