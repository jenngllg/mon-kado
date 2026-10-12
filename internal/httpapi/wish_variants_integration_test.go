//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestWishVariants_WhenCreatingAndEditing_PersistsAndSharesOptionalValues(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "variants@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Variants","occasion":"other"}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id + "/wishes"
	var wish domain.WishResponse
	// Act
	created := f.send(t, "POST", base, `{"name":"Product","color":" vert sauge ","size":" 140 × 200 cm ","quantity":1}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	path := base + "/" + wish.Id
	// Assert
	assertVariants := func(color, size *string) {
		t.Helper()
		if color == nil || *color != "vert sauge" || size == nil || *size != "140 × 200 cm" {
			t.Fatal("variants were not trimmed and persisted")
		}
	}
	assertVariants(wish.Color, wish.Size)
	requireStatus(t, f.send(t, "GET", path, "", nil), 200, &wish)
	assertVariants(wish.Color, wish.Size)
	var collection domain.WishCollectionResponse
	requireStatus(t, f.send(t, "GET", base, "", nil), 200, &collection)
	if len(collection.Wishes) != 1 {
		t.Fatal("wish collection differs")
	}
	assertVariants(collection.Wishes[0].Color, collection.Wishes[0].Size)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	share, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	visitor := f.visitor(t)
	headers := map[string]string{"X-MonKado-Share-Token": share.Fragment}
	var detail domain.SharedWishDetailResponse
	requireStatus(t, visitor.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id+"/wishes/"+wish.Id, "", headers), 200, &detail)
	assertVariants(detail.Color, detail.Size)
	var shared domain.SharedWishlistResponse
	requireStatus(t, visitor.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id, "", headers), 200, &shared)
	if len(shared.Wishes) != 1 {
		t.Fatal("shared collection differs")
	}
	assertVariants(shared.Wishes[0].Color, shared.Wishes[0].Size)
	updated := f.send(t, "PUT", path, `{"name":"Product","quantity":1,"color":"corail","size":"M"}`, map[string]string{"If-Match": etag})
	next := updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &wish)
	if wish.Color == nil || *wish.Color != "corail" || wish.Size == nil || *wish.Size != "M" || etag == next {
		t.Fatal("variant edit was not versioned")
	}
	requireStatus(t, f.send(t, "PUT", path, `{"name":"Product","quantity":1,"color":null,"size":null}`, map[string]string{"If-Match": etag}), 412, nil)
	requireStatus(t, f.send(t, "PUT", path, `{"name":"Product","quantity":1,"color":" ","size":null}`, map[string]string{"If-Match": next}), 200, &wish)
	requireStatus(t, f.send(t, "GET", path, "", nil), 200, &wish)
	if wish.Color != nil || wish.Size != nil {
		t.Fatal("cleared variants must remain explicit nulls")
	}
}
