//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestWishlistArchive_WhenArchivedAndRestored_PreservesContentAndReenablesTheSameShare(t *testing.T) {
	// Arrange
	f := newFixture(t)
	owner := f.member(t, "archive-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Archive me","occasion":"birthday","message":"Keep me"}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	createdWish := f.send(t, "POST", base+"/wishes", `{"name":"Retained wish","quantity":2}`, nil)
	wishETag := createdWish.Header.Get("ETag")
	requireStatus(t, createdWish, 201, &wish)
	requireStatus(t, f.upload(t, base+"/wishes/"+wish.Id+"/image", wishETag, 42), 200, &wish)
	var share domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", base+"/share-link", "", nil), 201, &share)
	link, err := url.Parse(share.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	sharedBase := "/api/v1" + link.Path
	shareHeader := map[string]string{"X-MonKado-Share-Token": link.Fragment}
	var sharedWish domain.SharedWishDetailResponse
	requireStatus(t, f.send(t, "GET", sharedBase+"/wishes/"+wish.Id, "", shareHeader), 200, &sharedWish)
	if sharedWish.ImageUrl == nil || wish.ImageUrl == nil {
		t.Fatal("fixture image is missing")
	}
	publicImage, err := url.Parse(*sharedWish.ImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	ownedImage, err := url.Parse(*wish.ImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	var reservation domain.GiftReservationResponse
	reservationPath := base + "/wishes/" + wish.Id + "/reservations/current"
	requireStatus(t, f.send(t, "PUT", reservationPath, `{"quantity":1}`, nil), 201, &reservation)
	current := f.send(t, "GET", base, "", nil)
	originalETag := current.Header.Get("ETag")
	requireStatus(t, current, 200, &list)
	// Act
	archived := f.send(t, "PATCH", base, `{"isArchived":true}`, map[string]string{"If-Match": originalETag})
	archivedETag := archived.Header.Get("ETag")
	var saved domain.WishlistResponse
	requireStatus(t, archived, 200, &saved)
	// Assert
	if !saved.IsArchived || saved.Id != list.Id || saved.Name != list.Name || saved.Message == nil || *saved.Message != "Keep me" || saved.UpdatedAt == nil || archivedETag == "" || archivedETag == originalETag {
		t.Fatal("archive did not preserve the full resource and advance its version")
	}
	var activeLists, archivedLists []domain.WishlistResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &activeLists)
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists?isArchived=true", "", nil), 200, &archivedLists)
	if len(activeLists) != 0 || len(archivedLists) != 1 || archivedLists[0].Id != list.Id || !archivedLists[0].IsArchived {
		t.Fatal("active and archived collections are not disjoint")
	}
	var profile domain.PublicMemberProfileResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil), 200, &profile)
	if len(profile.Wishlists) != 0 {
		t.Fatal("archived list remained publicly discoverable")
	}
	requireStatus(t, f.send(t, "GET", sharedBase, "", shareHeader), 404, nil)
	requireStatus(t, f.send(t, "GET", publicImage.RequestURI(), "", nil), 404, nil)
	requireStatus(t, f.send(t, "GET", ownedImage.RequestURI(), "", nil), 200, nil)
	var wishes domain.WishCollectionResponse
	requireStatus(t, f.send(t, "GET", base+"/wishes", "", nil), 200, &wishes)
	if len(wishes.Wishes) != 1 || wishes.Wishes[0].Id != wish.Id {
		t.Fatal("archive removed the wish")
	}
	var retained domain.GiftReservationResponse
	requireStatus(t, f.send(t, "GET", reservationPath, "", nil), 200, &retained)
	if retained.Id != reservation.Id || retained.Quantity != 1 {
		t.Fatal("archive altered the reservation")
	}
	for _, operation := range []struct{ method, path, body string }{
		{"POST", base + "/wishes", `{"name":"Rejected"}`},
		{"PUT", base, `{"name":"Rejected","occasion":"other"}`},
		{"PUT", reservationPath, `{"quantity":2}`},
	} {
		var failure domain.ErrorResponse
		requireStatus(t, f.send(t, operation.method, operation.path, operation.body, map[string]string{"If-Match": archivedETag}), 409, &failure)
		if failure.ErrorCode == nil || *failure.ErrorCode != domain.WishlistArchived {
			t.Fatal("archived content mutation was not blocked")
		}
	}
	unchanged := f.send(t, "PATCH", base, `{"isArchived":true}`, map[string]string{"If-Match": archivedETag})
	if unchanged.Header.Get("ETag") != archivedETag {
		t.Fatal("idempotent archive changed the ETag")
	}
	var same domain.WishlistResponse
	requireStatus(t, unchanged, 200, &same)
	if same.UpdatedAt == nil || !same.UpdatedAt.Equal(*saved.UpdatedAt) {
		t.Fatal("idempotent archive changed its timestamp")
	}
	requireStatus(t, f.send(t, "PATCH", base, `{"isArchived":false}`, map[string]string{"If-Match": originalETag}), 412, nil)
	// Restore without rotating the link or discarding a reservation.
	restored := f.send(t, "PATCH", base, `{"isArchived":false}`, map[string]string{"If-Match": archivedETag})
	if restored.Header.Get("ETag") == archivedETag {
		t.Fatal("restoration did not advance the version")
	}
	requireStatus(t, restored, 200, &saved)
	if saved.IsArchived {
		t.Fatal("restoration did not clear the archive flag")
	}
	var retainedShare domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "GET", base+"/share-link", "", nil), 200, &retainedShare)
	if retainedShare.ShareUrl != share.ShareUrl {
		t.Fatal("archive or restore rotated the share secret")
	}
	requireStatus(t, f.send(t, "GET", sharedBase, "", shareHeader), 200, nil)
	requireStatus(t, f.send(t, "GET", publicImage.RequestURI(), "", nil), 200, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil), 200, &profile)
	if len(profile.Wishlists) != 1 || profile.Wishlists[0].Id != list.Id {
		t.Fatal("restored list did not become discoverable again")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists?isArchived=true", "", nil), 200, &archivedLists)
	if len(archivedLists) != 0 {
		t.Fatal("restored list remained in the archived collection")
	}
}

func TestWishlistArchive_WhenInvalidForeignOrSuspended_PreservesVersionAndAuthorization(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "archive-security@example.test")
	ownerAccess := f.access
	var list domain.WishlistResponse
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Protected","occasion":"other"}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &list)
	path := "/api/v1/wishlists/" + list.Id
	// Act / Assert
	for _, scenario := range []struct {
		name, body, version string
		status              int
	}{
		{"missingVersion", `{"isArchived":true}`, "", 428},
		{"malformedVersion", `{"isArchived":true}`, "invalid", 400},
		{"staleVersion", `{"isArchived":true}`, `"00000000"`, 412},
		{"missingState", `{}`, etag, 400},
		{"nullState", `{"isArchived":null}`, etag, 400},
		{"textState", `{"isArchived":"true"}`, etag, 400},
		{"otherMetadata", `{"isArchived":true,"name":"Changed"}`, etag, 400},
		{"secondDocument", `{"isArchived":true}{}`, etag, 400},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			headers := map[string]string{}
			if scenario.version != "" {
				headers["If-Match"] = scenario.version
			}
			requireStatus(t, f.send(t, "PATCH", path, scenario.body, headers), scenario.status, nil)
		})
	}
	for _, query := range []string{"isArchived=invalid", "isArchived=", "isArchived=true&isArchived=false"} {
		requireStatus(t, f.send(t, "GET", "/api/v1/wishlists?"+query, "", nil), 400, nil)
	}
	f.access = ""
	requireStatus(t, f.send(t, "PATCH", path, `{"isArchived":true}`, map[string]string{"If-Match": etag}), 401, nil)
	f.csrfToken(t)
	f.member(t, "archive-other@example.test")
	requireStatus(t, f.send(t, "PATCH", path, `{"isArchived":true}`, map[string]string{"If-Match": etag}), 404, nil)
	f.access = ownerAccess
	requireStatus(t, f.send(t, "PATCH", "/api/v1/wishlists/"+security.NewID(), `{"isArchived":true}`, map[string]string{"If-Match": etag}), 404, nil)
	current := f.send(t, "GET", path, "", nil)
	if current.Header.Get("ETag") != etag {
		t.Fatal("rejected archive operation changed the version")
	}
	requireStatus(t, current, 200, &list)
	if list.IsArchived || list.Name != "Protected" || list.UpdatedAt != nil {
		t.Fatal("rejected archive operation changed the resource")
	}
	// Seed administrative suspension; owner mutations must not bypass it.
	if _, err := f.store.Pool.Exec(t.Context(), `UPDATE wishlists SET is_suspended=true,suspension_reason='Review',suspended_at=$2 WHERE id=$1`, list.Id, f.service.Now()); err != nil {
		t.Fatal(err)
	}
	current = f.send(t, "GET", path, "", nil)
	etag = current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	var failure domain.ErrorResponse
	requireStatus(t, f.send(t, "PATCH", path, `{"isArchived":true}`, map[string]string{"If-Match": etag}), 409, &failure)
	if failure.ErrorCode == nil || *failure.ErrorCode != domain.WishlistSuspended {
		t.Fatal("suspension guard was bypassed")
	}
}

func TestWishlistArchive_WhenArchivedListDeleted_AllowsOwnerDeletion(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "delete-archive@example.test")
	var list domain.WishlistResponse
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Delete archive","occasion":"other"}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &list)
	path := "/api/v1/wishlists/" + list.Id
	archived := f.send(t, "PATCH", path, `{"isArchived":true}`, map[string]string{"If-Match": etag})
	etag = archived.Header.Get("ETag")
	requireStatus(t, archived, 200, nil)
	// Act
	requireStatus(t, f.send(t, "DELETE", path, "", map[string]string{"If-Match": etag}), 204, nil)
	// Assert
	requireStatus(t, f.send(t, "GET", path, "", nil), 404, nil)
}
