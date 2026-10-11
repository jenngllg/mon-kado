//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestReservationHistory_WhenResourcesChange_UsesLiveContextAndPreservesPrivateNavigation(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "history-context-owner@example.test")
	var session domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &session)
	var list domain.WishlistResponse
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Original list","occasion":"other","surpriseMode":false}`, nil)
	listVersion := created.Header.Get("ETag")
	requireStatus(t, created, 201, &list)
	listPath := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	created = f.send(t, "POST", listPath+"/wishes", `{"name":"Original wish","quantity":3}`, nil)
	wishVersion := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	wishPath := listPath + "/wishes/" + wish.Id
	updated := f.upload(t, wishPath+"/image", wishVersion, 56)
	wishVersion = updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &wish)
	requireStatus(t, f.send(t, "PUT", wishPath+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", listPath+"/share-link", "", nil), 201, &link)
	u, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	participant := f.visitor(t)
	participant.member(t, "history-context-participant@example.test")
	headers := map[string]string{"X-MonKado-Share-Token": u.Fragment}
	sharedPath := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, participant.send(t, "POST", sharedPath+"/participants", "", headers), 201, nil)
	requireStatus(t, participant.send(t, "PUT", sharedPath+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, headers), 201, nil)
	history := func(client *fixture) domain.GiftReservationHistoryResponse {
		t.Helper()
		var page domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
		requireStatus(t, client.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &page)
		if len(page.Items) != 1 {
			t.Fatal("expected one private lifecycle")
		}
		return page.Items[0]
	}
	// Act
	owned := history(f)
	shared := history(participant)
	// Assert
	if owned.OwnerId == nil || *owned.OwnerId != session.Id || owned.OwnerDisplayName == nil || owned.OwnedWishPath == nil || *owned.OwnedWishPath != "/lists/"+list.Id+"/wishes/"+wish.Id || owned.ImageUrl == nil || owned.ShareUrl != nil || owned.IsArchived {
		t.Fatal("owned history navigation differs")
	}
	if shared.OwnedWishPath != nil || shared.ShareUrl == nil || *shared.ShareUrl != link.ShareUrl || shared.ImageUrl == nil {
		t.Fatal("shared history navigation differs")
	}
	updated = f.send(t, "PUT", listPath, `{"name":"Renamed list","occasion":"other"}`, map[string]string{"If-Match": listVersion})
	listVersion = updated.Header.Get("ETag")
	requireStatus(t, updated, 200, nil)
	updated = f.send(t, "PUT", wishPath, `{"name":"Renamed wish","quantity":3}`, map[string]string{"If-Match": wishVersion})
	wishVersion = updated.Header.Get("ETag")
	requireStatus(t, updated, 200, nil)
	if current := history(participant); current.WishlistName != "Renamed list" || current.WishName != "Renamed wish" {
		t.Fatal("history did not use live names")
	}
	updated = f.send(t, "PATCH", listPath, `{"isArchived":true}`, map[string]string{"If-Match": listVersion})
	listVersion = updated.Header.Get("ETag")
	requireStatus(t, updated, 200, nil)
	for _, client := range []*fixture{f, participant} {
		current := history(client)
		if !current.IsArchived || current.OwnedWishPath != nil || current.ShareUrl != nil || current.ImageUrl != nil || current.OwnerId == nil || current.ShareLinkId == nil {
			t.Fatal("archived history disclosed actionable navigation or lost context")
		}
	}
	updated = f.send(t, "PATCH", listPath, `{"isArchived":false}`, map[string]string{"If-Match": listVersion})
	requireStatus(t, updated, 200, nil)
	linkRead := f.send(t, "GET", listPath+"/share-link", "", nil)
	linkVersion := linkRead.Header.Get("ETag")
	requireStatus(t, linkRead, 200, nil)
	requireStatus(t, f.send(t, "DELETE", listPath+"/share-link", "", map[string]string{"If-Match": linkVersion}), 204, nil)
	if current := history(participant); current.ShareUrl != nil || current.ImageUrl != nil || current.ShareLinkId != nil {
		t.Fatal("revoked share remained actionable")
	}
	if current := history(f); current.OwnedWishPath == nil || current.ImageUrl == nil {
		t.Fatal("owner requires a share link")
	}
	requireStatus(t, f.send(t, "DELETE", wishPath, "", map[string]string{"If-Match": wishVersion}), 204, nil)
	for _, client := range []*fixture{f, participant} {
		current := history(client)
		if current.Status != "unavailable" || current.OwnedWishPath != nil || current.ImageUrl != nil || current.WishName != "Original wish" {
			t.Fatal("deleted wish lifecycle or retained snapshot differs")
		}
	}
}



