//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestWishlistPreferences_WhenArchiving_PreservesSharesAndRestrictsOwnerMutations(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "archive-owner@example.test")
	var list domain.WishlistResponse
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Archive list","occasion":"other","surpriseMode":false}`, nil)
	listVersion := created.Header.Get("ETag")
	requireStatus(t, created, 201, &list)
	listPath := "/api/v1/wishlists/" + list.Id
	wishesPath := listPath + "/wishes"
	if list.IsArchived || list.SurpriseMode || list.UpdatedAt != nil {
		t.Fatal("creation preferences differ")
	}
	var wish domain.WishResponse
	wishCreated := f.send(t, "POST", wishesPath, `{"name":"Favorite gift","quantity":3,"isFavorite":true}`, nil)
	wishVersion := wishCreated.Header.Get("ETag")
	requireStatus(t, wishCreated, 201, &wish)
	wishPath := wishesPath + "/" + wish.Id
	if !wish.IsFavorite || wish.ReservedQuantity == nil || *wish.ReservedQuantity != 0 || wish.AvailableQuantity == nil || *wish.AvailableQuantity != 3 || wish.UpdatedAt != nil {
		t.Fatal("favorite or visible quantity creation differs")
	}
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", listPath+"/share-link", "", nil), 201, &link)
	shareURL, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	shareHeaders := map[string]string{"X-MonKado-Share-Token": shareURL.Fragment}
	// Act
	archived := f.send(t, "PATCH", listPath, `{"isArchived":true}`, map[string]string{"If-Match": listVersion})
	archiveVersion := archived.Header.Get("ETag")
	requireStatus(t, archived, 200, &list)
	// Assert
	if !list.IsArchived || archiveVersion == listVersion || list.UpdatedAt == nil {
		t.Fatal("archive state or audit timestamp was not persisted")
	}
	var active, archivedLists []domain.WishlistResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &active)
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists?isArchived=true", "", nil), 200, &archivedLists)
	if len(active) != 0 || len(archivedLists) != 1 || archivedLists[0].Id != list.Id {
		t.Fatal("archive filtering differs")
	}
	unchanged := f.send(t, "PATCH", listPath, `{"isArchived":true}`, map[string]string{"If-Match": archiveVersion})
	if unchanged.Header.Get("ETag") != archiveVersion {
		t.Fatal("idempotent archive changed the version")
	}
	requireStatus(t, unchanged, 200, nil)
	requireStatus(t, f.send(t, "PATCH", listPath, `{"isArchived":false}`, map[string]string{"If-Match": listVersion}), 412, nil)
	requireStatus(t, f.send(t, "PATCH", listPath, `{"isArchived":false}`, nil), 428, nil)
	for _, invalid := range []string{`{}`, `{"isArchived":null}`, `{"isArchived":1}`} {
		requireStatus(t, f.send(t, "PATCH", listPath, invalid, map[string]string{"If-Match": archiveVersion}), 400, nil)
	}
	for _, invalid := range []string{"", "1", "maybe", "false&isArchived=true"} {
		requireStatus(t, f.send(t, "GET", "/api/v1/wishlists?isArchived="+invalid, "", nil), 400, nil)
	}
	for _, operation := range []struct{ method, path, body, version string }{
		{"POST", wishesPath, `{"name":"Blocked"}`, ""},
		{"PUT", listPath, `{"name":"Blocked","occasion":"other"}`, archiveVersion},
		{"PATCH", wishPath, `{"isFavorite":false}`, wishVersion},
		{"DELETE", wishPath, "", wishVersion},
		{"POST", listPath + "/share-link", "", ""},
	} {
		var failure domain.ErrorResponse
		requireStatus(t, f.send(t, operation.method, operation.path, operation.body, map[string]string{"If-Match": operation.version}), 409, &failure)
		if failure.ErrorCode == nil || *failure.ErrorCode != domain.WishlistArchived {
			t.Fatal("archived mutation returned the wrong code")
		}
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id, "", shareHeaders), 404, nil)
	restored := f.send(t, "PATCH", listPath, `{"isArchived":false}`, map[string]string{"If-Match": archiveVersion})
	listVersion = restored.Header.Get("ETag")
	requireStatus(t, restored, 200, &list)
	requireStatus(t, f.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id, "", shareHeaders), 200, nil)
	favorite := f.send(t, "PATCH", wishPath, `{"isFavorite":false}`, map[string]string{"If-Match": wishVersion})
	newWishVersion := favorite.Header.Get("ETag")
	requireStatus(t, favorite, 200, &wish)
	if wish.IsFavorite || newWishVersion == wishVersion || wish.Position != 1 || wish.Name != "Favorite gift" || wish.UpdatedAt == nil {
		t.Fatal("favorite update changed other content or lost its timestamp")
	}
	requireStatus(t, f.send(t, "PATCH", wishPath, `{"isFavorite":true}`, map[string]string{"If-Match": wishVersion}), 412, nil)
	repeated := f.send(t, "PATCH", wishPath, `{"isFavorite":false}`, map[string]string{"If-Match": newWishVersion})
	if repeated.Header.Get("ETag") != newWishVersion {
		t.Fatal("idempotent favorite changed the version")
	}
	requireStatus(t, repeated, 200, nil)
	var collection domain.WishCollectionResponse
	requireStatus(t, f.send(t, "GET", wishesPath, "", nil), 200, &collection)
	if len(collection.Wishes) != 1 || collection.Wishes[0].IsFavorite || collection.Wishes[0].AvailableQuantity == nil || *collection.Wishes[0].AvailableQuantity != 3 {
		t.Fatal("collection preferences differ from the individual resource")
	}
	archived = f.send(t, "PATCH", listPath, `{"isArchived":true}`, map[string]string{"If-Match": listVersion})
	archiveVersion = archived.Header.Get("ETag")
	requireStatus(t, archived, 200, nil)
	ownerAccess := f.access
	f.member(t, "foreign-archive-owner@example.test")
	requireStatus(t, f.send(t, "PATCH", listPath, `{"isArchived":false}`, map[string]string{"If-Match": archiveVersion}), 404, nil)
	f.access = ownerAccess
	requireStatus(t, f.send(t, "DELETE", listPath, "", map[string]string{"If-Match": archiveVersion}), 204, nil)
}

func TestWishlistSurpriseMode_WhenUpdating_PreservesHiddenQuantitiesAndNoOpVersions(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "surprise-owner@example.test")
	var list domain.WishlistResponse
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Surprise list","occasion":"other"}`, nil)
	version := created.Header.Get("ETag")
	requireStatus(t, created, 201, &list)
	path := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	wishCreated := f.send(t, "POST", path+"/wishes", `{"name":"Surprise gift","price":1.20}`, nil)
	wishVersion := wishCreated.Header.Get("ETag")
	requireStatus(t, wishCreated, 201, &wish)
	if !list.SurpriseMode || wish.ReservedQuantity != nil || wish.AvailableQuantity != nil {
		t.Fatal("default surprise mode disclosed reservation quantities")
	}
	// Act / Assert
	unchanged := f.send(t, "PUT", path, `{"name":"Surprise list","occasion":"other"}`, map[string]string{"If-Match": version})
	if unchanged.Header.Get("ETag") != version {
		t.Fatal("unchanged list metadata changed its version")
	}
	requireStatus(t, unchanged, 200, &list)
	if list.UpdatedAt != nil {
		t.Fatal("unchanged metadata changed the audit timestamp")
	}
	wishPath := path + "/wishes/" + wish.Id
	unchangedWish := f.send(t, "PUT", wishPath, `{"name":"Surprise gift","price":1.2}`, map[string]string{"If-Match": wishVersion})
	if unchangedWish.Header.Get("ETag") != wishVersion {
		t.Fatal("equivalent monetary value changed the wish version")
	}
	requireStatus(t, unchangedWish, 200, nil)
	f.service.Now = func() time.Time { return list.CreatedAt.Add(time.Second) }
	changed := f.send(t, "PUT", path, `{"name":"Surprise list","occasion":"other","surpriseMode":false}`, map[string]string{"If-Match": version})
	version = changed.Header.Get("ETag")
	requireStatus(t, changed, 200, &list)
	if list.SurpriseMode || list.UpdatedAt == nil {
		t.Fatal("surprise preference was not changed")
	}
	requireStatus(t, f.send(t, "GET", wishPath, "", nil), 200, &wish)
	if wish.ReservedQuantity == nil || *wish.ReservedQuantity != 0 || wish.AvailableQuantity == nil || *wish.AvailableQuantity != 1 {
		t.Fatal("disabled surprise mode did not expose quantities")
	}
	omitted := f.send(t, "PUT", path, `{"name":"Surprise renamed","occasion":"other"}`, map[string]string{"If-Match": version})
	requireStatus(t, omitted, 200, &list)
	if list.SurpriseMode {
		t.Fatal("omitted preference replaced the saved setting")
	}
}
