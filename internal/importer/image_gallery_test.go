package importer_test

import (
	"net/url"
	"reflect"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestExtract_WhenProductHasLongGallery_KeepsExplicitPagePreviewWithinBoundedAlternates(t *testing.T) {
	// Arrange
	base, _ := url.Parse("https://merchant.test/product")
	html := `<meta property="og:image" content="/optimized.jpg"><script type="application/ld+json">{"@type":"Product","name":"Gift","image":["/primary.jpg","/second.jpg","/third.jpg","/fourth.jpg","/fifth.jpg"]}</script>`
	// Act
	metadata := importer.Extract([]byte(html), base)
	// Assert
	want := []string{"https://merchant.test/primary.jpg", "https://merchant.test/optimized.jpg", "https://merchant.test/second.jpg", "https://merchant.test/third.jpg", "https://merchant.test/fourth.jpg"}
	if !reflect.DeepEqual(metadata.Images, want) || metadata.Image == nil || *metadata.Image != want[0] {
		t.Fatalf("primary or bounded preview alternative lost: %v", metadata.Images)
	}
}
