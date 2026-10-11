package importer_test

import (
	"context"
	"errors"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/importer"
)

type catalogDownloader struct {
	t           *testing.T
	parent      context.Context
	body, media string
	err         error
	calls       int
}

func (d *catalogDownloader) Download(ctx context.Context, address string, maximum int64) ([]byte, *url.URL, string, error) {
	d.t.Helper()
	d.calls++
	if ctx != d.parent || maximum != 256*1024 || !strings.HasPrefix(address, "https://ac.cnstrc.com/search/") {
		d.t.Fatal("catalog transport budget or context differs")
	}
	u, err := url.Parse(address)
	if err != nil || u.Query().Get("key") != "key_public" || u.Query().Get("num_results_per_page") != "3" {
		d.t.Fatal("catalog query differs")
	}
	return []byte(d.body), u, d.media, d.err
}

func TestProductCatalog_WhenAuthorityOrPathIsNotCanonical_RefusesWithoutNetwork(t *testing.T) {
	for _, test := range []struct {
		address       string
		enabled, want bool
	}{
		{"https://www.sephora.fr/p/product-12345.html", true, true},
		{"https://sephora.fr:443/p/P123.html", true, true},
		{"https://sephora.fr/p/123.html", false, false},
		{"http://sephora.fr/p/123.html", true, false},
		{"https://user@sephora.fr/p/123.html", true, false},
		{"https://sephora.fr:444/p/123.html", true, false},
		{"https://sephora.fr.evil.test/p/123.html", true, false},
		{"https://sephora.fr/p/1234567890123.html", true, false},
		{"https://sephora.fr/product/123", true, false},
	} {
		t.Run(test.address, func(t *testing.T) {
			// Arrange
			u, _ := url.Parse(test.address)
			c := &importer.ProductCatalog{Enabled: test.enabled}
			// Act
			actual := c.CanHandle(u)
			// Assert
			if actual != test.want {
				t.Fatal("catalog authority accepted incorrectly")
			}
			if !actual {
				result, err := c.Get(t.Context(), u)
				if err != nil || result.Name != nil {
					t.Fatal("unsupported source used a catalog")
				}
			}
		})
	}
}

func TestProductCatalog_WhenResultsAreFuzzyOrConflicting_UsesOnlyExactVariant(t *testing.T) {
	variant := `{"variation_id":"123","url":"https://www.sephora.fr/p/product-123.html","name":"Fragrance","image_url":"http://media.sephora.eu/image.jpg?old=1","price":{"salesPrice":29.90,"currency":"EUR"}}`
	for _, test := range []struct {
		name, path, body, wantName, wantPrice, wantImage, media string
		wantError                                               bool
	}{
		{"exact variant", "123", `{"response":{"results":[{"data":` + variant + `}]}}`, "Fragrance", "29.9", "https://media.sephora.eu/image.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit", "application/json", false},
		{"master explicit default", "P42", `{"response":{"results":[{"data":{"id":"P42","defaultVariantId":123},"variations":[{"data":` + variant + `}]}]}}`, "Fragrance", "29.9", "https://media.sephora.eu/image.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit", "application/json", false},
		{"master default must be numeric", "P42", `{"response":{"results":[{"data":{"id":"P42","defaultVariantId":"123"},"variations":[{"data":` + variant + `}]}]}}`, "", "", "", "application/json", false},
		{"master must be exact", "P43", `{"response":{"results":[{"data":{"id":"P42","defaultVariantId":123},"variations":[{"data":` + variant + `}]}]}}`, "", "", "", "application/json", false},
		{"fuzzy variant", "456", `{"response":{"results":[{"data":` + variant + `}]}}`, "", "", "", "application/json", false},
		{"untrusted URL", "123", strings.ReplaceAll(`{"response":{"results":[{"data":`+variant+`}]}}`, "www.sephora.fr", "www.sephora.fr.evil.test"), "", "", "", "application/json", false},
		{"identity disagreement", "123", strings.ReplaceAll(`{"response":{"results":[{"data":`+variant+`}]}}`, "product-123.html", "product-456.html"), "", "", "", "application/json", false},
		{"duplicates within result agree", "123", `{"response":{"results":[{"data":` + variant + `,"variations":[{"data":` + variant + `}]}]}}`, "Fragrance", "29.9", "https://media.sephora.eu/image.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit", "application/json", false},
		{"conflicting variant", "123", `{"response":{"results":[{"data":` + variant + `,"variations":[{"data":` + strings.ReplaceAll(variant, "29.90", "39.90") + `}]}]}}`, "", "", "", "application/json", false},
		{"multiple exact results", "123", `{"response":{"results":[{"data":` + variant + `},{"data":` + variant + `}]}}`, "", "", "", "application/json", false},
		{"non first party image", "123", strings.ReplaceAll(`{"response":{"results":[{"data":`+variant+`}]}}`, "media.sephora.eu", "images.other.test"), "Fragrance", "29.9", "", "application/json", false},
		{"credentials in image", "123", strings.ReplaceAll(`{"response":{"results":[{"data":`+variant+`}]}}`, "http://media.sephora.eu", "http://user@media.sephora.eu"), "Fragrance", "29.9", "", "application/json", false},
		{"image port rejected", "123", strings.ReplaceAll(`{"response":{"results":[{"data":`+variant+`}]}}`, "media.sephora.eu/image", "media.sephora.eu:999/image"), "Fragrance", "29.9", "", "application/json", false},
		{"empty catalog", "123", `{"response":{"results":[]}}`, "", "", "", "application/json", false},
		{"unexpected shape", "123", `{"response":[]}`, "", "", "", "application/json", false},
		{"unexpected variant types", "123", `{"response":{"results":[null,42,{"data":false}]}}`, "", "", "", "application/json", false},
		{"invalid JSON", "123", `{"response":`, "", "", "", "application/json", true},
		{"invalid media", "123", `{}`, "", "", "", "text/html", true},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			u, _ := url.Parse("https://www.sephora.fr/p/product-" + test.path + ".html")
			downloader := &catalogDownloader{t: t, parent: t.Context(), body: test.body, media: test.media}
			c := &importer.ProductCatalog{Client: downloader, Enabled: true, IndexKey: "key_public"}
			// Act
			result, err := c.Get(t.Context(), u)
			// Assert
			if (err != nil) != test.wantError || downloader.calls != 1 {
				t.Fatalf("catalog call/error differs: %v", err)
			}
			name, price, image := "", "", ""
			if result.Name != nil {
				name = *result.Name
			}
			if result.Price != nil {
				price = result.Price.String()
			}
			if result.Image != nil {
				image = *result.Image
			}
			if name != test.wantName || price != test.wantPrice || image != test.wantImage {
				t.Fatal("catalog chose another variant or invalid metadata")
			}
		})
	}
	// Arrange
	u, _ := url.Parse("https://sephora.fr/p/123.html")
	downloader := &catalogDownloader{t: t, parent: t.Context(), err: errors.New("remote failure")}
	c := &importer.ProductCatalog{Client: downloader, Enabled: true, IndexKey: "key_public"}
	// Act
	_, err := c.Get(t.Context(), u)
	// Assert
	if !errors.Is(err, downloader.err) {
		t.Fatal("transport failure was discarded")
	}
}
