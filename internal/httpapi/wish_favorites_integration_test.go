//go:build integration

package httpapi_test

import (
	"net/url"
	"reflect"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestWishFavorite_WhenChanged_PersistsAcrossViewsWithoutChangingContentOrOrder(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "favorites@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Favorites","occasion":"other"}`, nil), 201, &list)
	collectionPath := "/api/v1/wishlists/" + list.Id + "/wishes"
	var wish domain.WishResponse
	created := f.send(t, "POST", collectionPath, `{"name":"Original wish","note":"Keep this description","price":12.34,"quantity":3,"url":"https://merchant.example.test/product"}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	if wish.IsFavorite || wish.UpdatedAt != nil {
		t.Fatal("creation default changed")
	}
	path := collectionPath + "/" + wish.Id
	var before domain.WishCollectionResponse
	collection := f.send(t, "GET", collectionPath, "", nil)
	collectionTag := collection.Header.Get("ETag")
	requireStatus(t, collection, 200, &before)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	share, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	visitor := f.visitor(t)
	headers := map[string]string{"X-MonKado-Share-Token": share.Fragment}
	for _, favorite := range []bool{true, false, true} {
		// Act
		var saved domain.WishResponse
		updated := f.send(t, "PATCH", path, jsonInput(t, map[string]bool{"isFavorite": favorite}), map[string]string{"If-Match": etag})
		next := updated.Header.Get("ETag")
		requireStatus(t, updated, 200, &saved)
		// Assert
		if saved.IsFavorite != favorite || next == etag || next == "" || saved.UpdatedAt == nil {
			t.Fatal("favorite preference or version was not saved")
		}
		unchanged := saved
		unchanged.IsFavorite, unchanged.UpdatedAt = wish.IsFavorite, wish.UpdatedAt
		if !reflect.DeepEqual(unchanged, wish) {
			t.Fatal("favorite command changed unrelated wish fields")
		}
		requireStatus(t, f.send(t, "PATCH", path, `{"isFavorite":false}`, map[string]string{"If-Match": etag}), 412, nil)
		etag = next
		var reread domain.WishResponse
		requireStatus(t, f.send(t, "GET", path, "", nil), 200, &reread)
		if reread.IsFavorite != favorite {
			t.Fatal("detail lost favorite preference")
		}
		var after domain.WishCollectionResponse
		collection = f.send(t, "GET", collectionPath, "", nil)
		if collection.Header.Get("ETag") == collectionTag || collection.Header.Get("ETag") == "" {
			t.Fatal("collection version did not reflect the changed wish")
		}
		collectionTag = collection.Header.Get("ETag")
		requireStatus(t, collection, 200, &after)
		if len(after.Wishes) != 1 || after.Wishes[0].IsFavorite != favorite || after.Wishes[0].Position != before.Wishes[0].Position || after.Wishes[0].EntityTag != etag {
			t.Fatal("collection lost favorite preference, position or version")
		}
		var shared domain.SharedWishlistResponse
		requireStatus(t, visitor.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id, "", headers), 200, &shared)
		if len(shared.Wishes) != 1 || shared.Wishes[0].IsFavorite != favorite {
			t.Fatal("shared gallery lost owner's favorite")
		}
		var sharedWish domain.SharedWishDetailResponse
		requireStatus(t, visitor.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id+"/wishes/"+wish.Id, "", headers), 200, &sharedWish)
		if sharedWish.IsFavorite != favorite {
			t.Fatal("shared detail lost owner's favorite")
		}
	}
}

func TestWishFavorite_WhenRequestUnauthorizedOrInvalid_RejectsWithoutMutation(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "favorite-owner@example.test")
	var list, otherList domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Favorites","occasion":"other"}`, nil), 201, &list)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Other","occasion":"other"}`, nil), 201, &otherList)
	var wish domain.WishResponse
	created := f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Wish"}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	path := "/api/v1/wishlists/" + list.Id + "/wishes/" + wish.Id
	for _, test := range []struct {
		name, body, tag string
		status          int
	}{
		{"missingTag", `{"isFavorite":true}`, "", 428},
		{"malformedTag", `{"isFavorite":true}`, "invalid", 400},
		{"staleTag", `{"isFavorite":true}`, `"00000000"`, 412},
		{"missingPreference", `{}`, etag, 400},
		{"nullPreference", `{"isFavorite":null}`, etag, 400},
		{"stringPreference", `{"isFavorite":"true"}`, etag, 400},
		{"numericPreference", `{"isFavorite":1}`, etag, 400},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Act / Assert
			headers := map[string]string{}
			if test.tag != "" {
				headers["If-Match"] = test.tag
			}
			requireStatus(t, f.send(t, "PATCH", path, test.body, headers), test.status, nil)
		})
	}
	// Act / Assert
	requireStatus(t, f.send(t, "PATCH", "/api/v1/wishlists/"+otherList.Id+"/wishes/"+wish.Id, `{"isFavorite":true}`, map[string]string{"If-Match": etag}), 404, nil)
	requireStatus(t, f.send(t, "PATCH", "/api/v1/wishlists/"+list.Id+"/wishes/"+security.NewID(), `{"isFavorite":true}`, map[string]string{"If-Match": etag}), 404, nil)
	visitor := f.visitor(t)
	requireStatus(t, visitor.send(t, "PATCH", path, `{"isFavorite":true}`, map[string]string{"If-Match": etag}), 401, nil)
	visitor.member(t, "favorite-stranger@example.test")
	requireStatus(t, visitor.send(t, "PATCH", path, `{"isFavorite":true}`, map[string]string{"If-Match": etag}), 404, nil)
	current := f.send(t, "GET", path, "", nil)
	if current.Header.Get("ETag") != etag {
		t.Fatal("rejected favorite command changed version")
	}
	requireStatus(t, current, 200, &wish)
	if wish.IsFavorite {
		t.Fatal("rejected favorite command changed preference")
	}
}

func TestWishFavorite_WhenSavedWithForm_PreservesOrUpdatesExplicitPreference(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "favorite-form@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Favorites","occasion":"other"}`, nil), 201, &list)
	path := "/api/v1/wishlists/" + list.Id + "/wishes"
	var wish domain.WishResponse
	created := f.send(t, "POST", path, `{"name":"Favorite","isFavorite":true}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	if !wish.IsFavorite {
		t.Fatal("creation ignored favorite preference")
	}
	path += "/" + wish.Id
	for _, test := range []struct {
		body     string
		favorite bool
	}{
		{`{"name":"Renamed"}`, true},
		{`{"name":"Renamed","isFavorite":false}`, false},
		{`{"name":"Renamed","isFavorite":true}`, true},
	} {
		// Act
		updated := f.send(t, "PUT", path, test.body, map[string]string{"If-Match": etag})
		etag = updated.Header.Get("ETag")
		requireStatus(t, updated, 200, &wish)
		// Assert
		if wish.IsFavorite != test.favorite {
			t.Fatal("form update lost or ignored favorite preference")
		}
	}
}
