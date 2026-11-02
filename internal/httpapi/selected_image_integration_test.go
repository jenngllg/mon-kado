//go:build integration

package httpapi_test

import (
	"context"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

type selectedImageResolverFake struct {
	calls int
	value string
}

func (f *selectedImageResolverFake) Resolve(ctx context.Context, value string) (domain.WishImportImage, error) {
	f.calls++
	f.value = value
	return domain.WishImportImage{ContentType: "image/webp", ContentBase64: "QUJD"}, ctx.Err()
}
func TestResolveWishImage_WhenOwnerSelectsImage_RespectsAuthenticationAndOwnership(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "selected-image-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"List","occasion":"other"}`, nil), 201, &list)
	path := "/api/v1/wishlists/" + list.Id + "/wish-images/resolve"
	provider := &selectedImageResolverFake{}
	f.server.Config.Handler.(*httpapi.API).ImageImporter = provider
	// Act and assert
	var image domain.WishImportImage
	requireStatus(t, f.send(t, "POST", path, `{"url":"https://images.example/product.png"}`, nil), 200, &image)
	if image.ContentType != "image/webp" || image.ContentBase64 != "QUJD" || provider.calls != 1 || provider.value != "https://images.example/product.png" {
		t.Fatal("selected image contract failed")
	}
	requireStatus(t, f.send(t, "POST", path, `{"url":"not a url"}`, nil), 400, nil)
	f.member(t, "selected-image-foreign@example.test")
	requireStatus(t, f.send(t, "POST", path, `{"url":"https://images.example/product.png"}`, nil), 404, nil)
	if provider.calls != 1 {
		t.Fatal("provider called for invalid or foreign input")
	}
	f.access = ""
	requireStatus(t, f.send(t, "POST", path, `{"url":"https://images.example/product.png"}`, nil), 401, nil)
	if provider.calls != 1 {
		t.Fatal("provider called without authentication")
	}
}

func TestResolveGroupSuggestionImage_WhenAccessChanges_PreservesGroupPrivacy(t *testing.T) {
	// Arrange
	s, listID, _ := suggestionScenario(t)
	path := s.base + "/wishlists/" + listID + "/suggestion-images/resolve"
	provider := &selectedImageResolverFake{}
	s.owner.server.Config.Handler.(*httpapi.API).ImageImporter = provider
	s.member.server.Config.Handler.(*httpapi.API).ImageImporter = provider
	s.foreign.server.Config.Handler.(*httpapi.API).ImageImporter = provider
	input := `{"url":"https://images.example/product.png"}`
	// Act and assert: the member can prepare a photo without creating a suggestion.
	var image domain.WishImportImage
	requireStatus(t, s.member.send(t, "POST", path, input, nil), 200, &image)
	if provider.calls != 1 || image.ContentType != "image/webp" {
		t.Fatal("authorized selected image was not returned")
	}
	requireStatus(t, s.member.send(t, "POST", path, input, map[string]string{"X-CSRF-TOKEN": ""}), 400, nil)
	requireStatus(t, s.owner.send(t, "POST", path, input, nil), 404, nil)
	requireStatus(t, s.member.send(t, "POST", path, `{"url":"invalid"}`, nil), 400, nil)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.foreignID, "", nil), 204, nil)
	requireStatus(t, s.foreign.send(t, "POST", path, input, nil), 404, nil)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/wishlists/"+listID, "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "POST", path, input, nil), 404, nil)
	s.member.access = ""
	requireStatus(t, s.member.send(t, "POST", path, input, nil), 401, nil)
	if provider.calls != 1 {
		t.Fatal("resolver called after denied access or invalid input")
	}
}
