//go:build integration

package httpapi_test

import (
	"bytes"
	"github.com/JennGllg/mon-kado/internal/domain"
	"image"
	"io"
	"net/url"
	"testing"
)

func TestImageFormats_WhenEfficientStorageIsEnabled_PreservesLegacyRoutesAndUsesTypedContent(t *testing.T) {
	// Arrange
	s, _, collection := suggestionScenario(t)
	s.member.service.Config.FastImageEncoding = true
	var idea domain.GroupSuggestionResponse
	created := s.member.send(t, "POST", collection, `{"name":"Format compatibility","quantity":1}`, nil)
	version := created.Header.Get("ETag")
	requireStatus(t, created, 201, &idea)
	path := collection + "/" + idea.Id + "/image"
	requireStatus(t, s.member.upload(t, path, version, 42, map[string]string{"X-CSRF-TOKEN": s.member.csrf}), 200, nil)
	// Act / Assert
	for _, tc := range []struct{ suffix, format string }{{"", "webp"}, {"/content", "png"}, {"/content?size=thumbnail", "png"}} {
		var body struct {
			Content []byte `json:"content"`
		}
		requireStatus(t, s.member.send(t, "GET", path+tc.suffix, "", nil), 200, &body)
		shape, format, err := image.DecodeConfig(bytes.NewReader(body.Content))
		if err != nil || format != tc.format || shape.Width > 1600 || shape.Height > 1600 {
			t.Fatal("image contract or bounds changed")
		}
		requireStatus(t, s.owner.send(t, "GET", path+tc.suffix, "", nil), 404, nil)
	}
}

func TestWishImageFormats_WhenEfficientStorageIsEnabled_ServesActualMediaType(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.service.Config.FastImageEncoding = true
	f.member(t, "imageformats@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Image formats","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	created := f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Format fixture"}`, nil)
	version := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	// Act
	requireStatus(t, f.upload(t, "/api/v1/wishlists/"+list.Id+"/wishes/"+wish.Id+"/image", version, 42), 200, &wish)
	// Assert
	location, parseErr := url.Parse(*wish.ImageUrl)
	if parseErr != nil {
		t.Fatal(parseErr)
	}
	response := f.send(t, "GET", location.RequestURI(), "", nil)
	defer response.Body.Close()
	content, err := io.ReadAll(response.Body)
	if err != nil || response.StatusCode != 200 || response.Header.Get("Content-Type") != "image/png" || !bytes.HasPrefix(content, []byte("\x89PNG\r\n\x1a\n")) {
		t.Fatal("stored image MIME does not match its bytes")
	}
}
