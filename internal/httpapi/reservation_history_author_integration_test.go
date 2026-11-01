//go:build integration

package httpapi_test

import (
	"encoding/json"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestReservationHistory_WhenAuthorIsDeleted_AnonymizesPreservedHistory(t *testing.T) {
	// Arrange
	owner := newFixture(t)
	owner.member(t, "historical-author@example.test")
	var list domain.WishlistResponse
	requireStatus(t, owner.send(t, "POST", "/api/v1/wishlists", `{"name":"History","occasion":"other"}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	requireStatus(t, owner.send(t, "POST", base+"/wishes", `{"name":"Wish","quantity":1}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	shared := owner.send(t, "POST", base+"/share-link", "", nil)
	shareTag := shared.Header.Get("ETag")
	requireStatus(t, shared, 201, &link)
	parsed, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": parsed.Fragment}
	reader := owner.visitor(t)
	reader.member(t, "historical-reader@example.test")
	sharedBase := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, reader.send(t, "POST", sharedBase+"/participants", "", headers), 201, nil)
	requireStatus(t, reader.send(t, "PUT", sharedBase+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, headers), 201, nil)
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].OwnerDisplayName == nil {
		t.Fatal("missing accessible author")
	}
	name := *history.Items[0].OwnerDisplayName
	if history.Items[0].OwnerId == nil {
		t.Fatal("missing accessible author profile")
	}
	ownerID := *history.Items[0].OwnerId
	// Act: revoke access and remove the list before the author deletes their account.
	requireStatus(t, owner.send(t, "DELETE", base+"/share-link", "", map[string]string{"If-Match": shareTag}), 204, nil)
	requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	item := history.Items[0]
	if item.OwnerDisplayName == nil || *item.OwnerDisplayName != name || item.OwnerId == nil || *item.OwnerId != ownerID || item.ShareUrl != nil || item.ImageUrl != nil {
		t.Fatal("inaccessible source must retain author profile without source access")
	}
	requireStatus(t, reader.send(t, "GET", "/api/v1/members/"+ownerID+"/profile", "", nil), 200, nil)
	listRead := owner.send(t, "GET", base, "", nil)
	listTag := listRead.Header.Get("ETag")
	requireStatus(t, listRead, 200, nil)
	requireStatus(t, owner.send(t, "DELETE", base, "", map[string]string{"If-Match": listTag}), 204, nil)
	requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if history.Items[0].OwnerDisplayName == nil || *history.Items[0].OwnerDisplayName != name || history.Items[0].OwnerId == nil || *history.Items[0].OwnerId != ownerID {
		t.Fatal("deleted list lost its historical author")
	}
	requireStatus(t, owner.send(t, "POST", "/api/v1/members/current/deletion-requests", "", nil), 202, nil)
	var outbox string
	if err := owner.store.Pool.QueryRow(t.Context(), `SELECT id::text FROM authentication_email_outbox WHERE kind='ACCOUNT_DELETION_CONFIRMATION'`).Scan(&outbox); err != nil {
		t.Fatal(err)
	}
	message, err := owner.service.PrepareAuthenticationEmail(t.Context(), outbox)
	if err != nil {
		t.Fatal(err)
	}
	action, err := url.Parse(message.ActionURL)
	if err != nil {
		t.Fatal(err)
	}
	body, err := json.Marshal(map[string]string{"token": actionCredentials(t, action).Get("token")})
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, owner.send(t, "POST", "/api/v1/members/current/deletion-requests/confirm", string(body), nil), 204, nil)
	// Assert: another member's history survives, without identifying deleted account data.
	requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if len(history.Items) != 1 {
		t.Fatal("reader history was deleted")
	}
	item = history.Items[0]
	if item.OwnerDisplayName != nil || item.OwnerId != nil || item.WishlistName != "Deleted wishlist" || item.WishName != "Deleted gift" {
		t.Fatal("deleted author data remains in history")
	}
}

func TestReservationHistory_WhenGroupWishAuthorIsDeleted_RemovesAuthorLink(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Group list","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Group wish","quantity":1}`, nil), 201, &wish)
	groupList := s.base + "/wishlists/" + list.Id
	requireStatus(t, s.owner.send(t, "PUT", groupList, "", nil), 201, nil)
	requireStatus(t, s.member.send(t, "PUT", groupList+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].OwnerId == nil || *history.Items[0].OwnerId != s.ownerID {
		t.Fatal("group reservation is missing its author profile")
	}
	// Act
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/members/current/deletion-requests", "", nil), 202, nil)
	var outbox string
	if err := s.owner.store.Pool.QueryRow(t.Context(), `SELECT id::text FROM authentication_email_outbox WHERE kind='ACCOUNT_DELETION_CONFIRMATION'`).Scan(&outbox); err != nil {
		t.Fatal(err)
	}
	message, err := s.owner.service.PrepareAuthenticationEmail(t.Context(), outbox)
	if err != nil {
		t.Fatal(err)
	}
	action, err := url.Parse(message.ActionURL)
	if err != nil {
		t.Fatal(err)
	}
	body, err := json.Marshal(map[string]string{"token": actionCredentials(t, action).Get("token")})
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/members/current/deletion-requests/confirm", string(body), nil), 204, nil)
	// Assert
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].OwnerId != nil || history.Items[0].OwnerDisplayName != nil || history.Items[0].GroupWishPath != nil || history.Items[0].WishlistName != "Deleted wishlist" || history.Items[0].WishName != "Deleted gift" {
		t.Fatal("group reservation must survive without deleted author identity or wish access")
	}
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/"+s.ownerID+"/profile", "", nil), 404, nil)
}

func TestReservationHistory_WhenGroupAccessEnds_KeepsAuthorProfile(t *testing.T) {
	for _, action := range []string{"delete", "leave"} {
		t.Run(action, func(t *testing.T) {
			// Arrange
			s := newGroupScenario(t)
			s.accept(t, s.member, s.memberID)
			var list domain.WishlistResponse
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Group list","occasion":"other"}`, nil), 201, &list)
			var wish domain.WishResponse
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Group wish","quantity":1}`, nil), 201, &wish)
			groupList := s.base + "/wishlists/" + list.Id
			requireStatus(t, s.owner.send(t, "PUT", groupList, "", nil), 201, nil)
			requireStatus(t, s.member.send(t, "PUT", groupList+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
			// Act
			if action == "delete" {
				group := s.owner.send(t, "GET", s.base, "", nil)
				etag := group.Header.Get("ETag")
				requireStatus(t, group, 200, nil)
				requireStatus(t, s.owner.send(t, "DELETE", s.base, "", map[string]string{"If-Match": etag}), 204, nil)
			} else {
				requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
			}
			// Assert
			var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
			requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
			if len(history.Items) != 1 || history.Items[0].OwnerId == nil || *history.Items[0].OwnerId != s.ownerID || history.Items[0].OwnerDisplayName == nil || history.Items[0].GroupWishPath != nil || history.Items[0].ShareUrl != nil {
				t.Fatal("group deletion must preserve the author profile without group wish access")
			}
			requireStatus(t, s.member.send(t, "GET", "/api/v1/members/"+s.ownerID+"/profile", "", nil), 200, nil)
		})
	}
}
