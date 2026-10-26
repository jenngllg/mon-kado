//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestWishPreferences_WhenCreatedSharedAndEdited_PreservesPreferencesAndReservations(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "wish-preferences@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Ideas","occasion":"other"}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id + "/wishes"
	var wish domain.WishResponse
	created := f.send(t, "POST", base, `{"name":"A relaxing experience","note":"A massage or creative workshop","price":40.25,"quantity":2,"isSurprise":true,"acceptsSecondHand":true}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	path := base + "/" + wish.Id
	// Act / Assert: the public contracts round-trip a budget without binary conversion.
	assertWish := func(value domain.WishResponse) {
		t.Helper()
		if !value.IsSurprise || !value.AcceptsSecondHand || value.Price == nil || value.Price.String() != "40.25" || value.Note == nil || *value.Note != "A massage or creative workshop" {
			t.Fatal("wish preferences or maximum budget were lost")
		}
	}
	assertWish(wish)
	requireStatus(t, f.send(t, "GET", path, "", nil), 200, &wish)
	assertWish(wish)
	var collection domain.WishCollectionResponse
	requireStatus(t, f.send(t, "GET", base, "", nil), 200, &collection)
	if len(collection.Wishes) != 1 || !collection.Wishes[0].IsSurprise || !collection.Wishes[0].AcceptsSecondHand {
		t.Fatal("collection lost preferences")
	}
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	u, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	visitor := f.visitor(t)
	headers := map[string]string{"X-MonKado-Share-Token": u.Fragment}
	sharedPath := "/api/v1/shared-wishlists/" + link.Id
	var detail domain.SharedWishDetailResponse
	requireStatus(t, visitor.send(t, "GET", sharedPath+"/wishes/"+wish.Id, "", headers), 200, &detail)
	if !detail.IsSurprise || !detail.AcceptsSecondHand || detail.Price == nil || detail.Price.String() != "40.25" {
		t.Fatal("shared detail lost preferences")
	}
	var shared domain.SharedWishlistResponse
	requireStatus(t, visitor.send(t, "GET", sharedPath, "", headers), 200, &shared)
	if len(shared.Wishes) != 1 || !shared.Wishes[0].IsSurprise || !shared.Wishes[0].AcceptsSecondHand {
		t.Fatal("shared collection lost preferences")
	}
	// Open-ended wishes keep normal quantity limits and versioned cancellation.
	var reservation domain.GiftReservationResponse
	reserved := f.send(t, "PUT", path+"/reservations/current", `{"quantity":2}`, nil)
	reservationTag := reserved.Header.Get("ETag")
	requireStatus(t, reserved, 201, &reservation)
	requireStatus(t, f.send(t, "PUT", path, `{"name":"Idea","quantity":1,"isSurprise":true}`, map[string]string{"If-Match": etag}), 409, nil)
	updated := f.send(t, "PUT", path, `{"name":"Idea","quantity":2,"price":40.25}`, map[string]string{"If-Match": etag})
	next := updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &wish)
	if !wish.IsSurprise || !wish.AcceptsSecondHand || next == etag {
		t.Fatal("omitted flags must preserve saved values and advance version")
	}
	requireStatus(t, f.send(t, "PUT", path, `{"name":"Idea","quantity":2,"isSurprise":false}`, map[string]string{"If-Match": etag}), 412, nil)
	requireStatus(t, f.send(t, "PUT", path, `{"name":"Specific product","quantity":2,"isSurprise":false,"acceptsSecondHand":false}`, map[string]string{"If-Match": next}), 200, &wish)
	if wish.IsSurprise || wish.AcceptsSecondHand {
		t.Fatal("explicit false flags were not saved")
	}
	requireStatus(t, f.send(t, "GET", path+"/reservations/current", "", nil), 200, &reservation)
	if reservation.Quantity != 2 {
		t.Fatal("editing gift preferences changed reservations")
	}
	requireStatus(t, f.send(t, "DELETE", path+"/reservations/current", "", map[string]string{"If-Match": reservationTag}), 204, nil)
}

func TestWishPreferences_WhenAbsentOrMalformed_DefaultsSafelyAndRejectsInvalidTypes(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "wish-defaults@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Defaults","occasion":"other"}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id + "/wishes"
	// Act / Assert
	for _, body := range []string{`{"name":"Regular"}`, `{"name":"Null flags","isSurprise":null,"acceptsSecondHand":null}`} {
		var wish domain.WishResponse
		requireStatus(t, f.send(t, "POST", base, body, nil), 201, &wish)
		if wish.IsSurprise || wish.AcceptsSecondHand {
			t.Fatal("legacy wishes must default to regular, unspecified condition")
		}
	}
	for _, body := range []string{`{"name":"Invalid","isSurprise":"true"}`, `{"name":"Invalid","acceptsSecondHand":1}`, `{"name":"Invalid","isSurprise":[]}`} {
		requireStatus(t, f.send(t, "POST", base, body, nil), 400, nil)
	}
}
