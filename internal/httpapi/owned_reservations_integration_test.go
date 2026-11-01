//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestOwnedReservation_WhenOtherParticipantsReserve_SharesCapacityAndPreservesPrivateAccess(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owned-reservation-owner@example.test")
	var list domain.WishlistResponse
	createdList := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Owned reservations","occasion":"other","surpriseMode":false}`, nil)
	listVersion := createdList.Header.Get("ETag")
	requireStatus(t, createdList, 201, &list)
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Three gifts","quantity":3}`, nil), 201, &wish)
	ownerPath := "/api/v1/wishlists/" + list.Id
	wishPath := ownerPath + "/wishes/" + wish.Id
	reservationPath := wishPath + "/reservations/current"
	visitor := f.visitor(t)
	requireStatus(t, visitor.send(t, "GET", reservationPath, "", nil), 401, nil)
	requireStatus(t, f.send(t, "GET", reservationPath, "", nil), 404, nil)
	requireStatus(t, f.send(t, "GET", ownerPath+"/wishes/"+security.NewID()+"/reservations/current", "", nil), 404, nil)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", ownerPath+"/share-link", "", nil), 201, &link)
	shareURL, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": shareURL.Fragment}
	sharedPath := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, visitor.send(t, "POST", sharedPath+"/participants", `{"displayName":"Other giver"}`, headers), 201, nil)
	requireStatus(t, visitor.send(t, "PUT", sharedPath+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":2}`, headers), 201, nil)
	// Act
	var reservation domain.GiftReservationResponse
	created := f.send(t, "PUT", reservationPath, `{"quantity":1}`, nil)
	version := created.Header.Get("ETag")
	location := created.Header.Get("Location")
	requireStatus(t, created, 201, &reservation)
	if location != reservationPath {
		t.Fatal("owned reservation Location differs")
	}
	// Assert
	if reservation.WishId != wish.Id || reservation.Quantity != 1 || reservation.UpdatedAt != nil {
		t.Fatal("owned reservation creation differs")
	}
	requireStatus(t, f.send(t, "GET", reservationPath, "", nil), 200, &reservation)
	requireStatus(t, f.send(t, "PUT", reservationPath, `{"quantity":1}`, nil), 428, nil)
	requireStatus(t, f.send(t, "PUT", reservationPath, `{"quantity":1}`, map[string]string{"If-Match": `"00000000"`}), 412, nil)
	requireStatus(t, f.send(t, "PUT", reservationPath, `{"quantity":1}`, map[string]string{"If-Match": "broken"}), 400, nil)
	requireStatus(t, f.send(t, "PUT", reservationPath, `{"quantity":2}`, map[string]string{"If-Match": version}), 409, nil)
	unchanged := f.send(t, "PUT", reservationPath, `{"quantity":1}`, map[string]string{"If-Match": version})
	if unchanged.Header.Get("ETag") != version {
		t.Fatal("unchanged reservation changed the version")
	}
	requireStatus(t, unchanged, 200, &reservation)
	if reservation.UpdatedAt != nil {
		t.Fatal("unchanged reservation changed its timestamp")
	}
	requireStatus(t, f.send(t, "GET", wishPath, "", nil), 200, &wish)
	if wish.ReservedQuantity == nil || *wish.ReservedQuantity != 3 || wish.AvailableQuantity == nil || *wish.AvailableQuantity != 0 {
		t.Fatal("owner and participant quantities did not share capacity")
	}
	stranger := f.visitor(t)
	stranger.member(t, "owned-reservation-stranger@example.test")
	for _, operation := range []struct{ method, body string }{{"GET", ""}, {"PUT", `{"quantity":1}`}, {"DELETE", ""}} {
		requireStatus(t, stranger.send(t, operation.method, reservationPath, operation.body, map[string]string{"If-Match": version}), 404, nil)
	}
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/reservations?status=active", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].Id != reservation.Id || history.Items[0].Quantity != 1 {
		t.Fatal("owned history included another participant's reservation")
	}
	archived := f.send(t, "PATCH", ownerPath, `{"isArchived":true}`, map[string]string{"If-Match": listVersion})
	listVersion = archived.Header.Get("ETag")
	requireStatus(t, archived, 200, nil)
	requireStatus(t, f.send(t, "GET", reservationPath, "", nil), 409, nil)
	requireStatus(t, f.send(t, "DELETE", reservationPath, "", map[string]string{"If-Match": version}), 409, nil)
	requireStatus(t, f.send(t, "PATCH", ownerPath, `{"isArchived":false}`, map[string]string{"If-Match": listVersion}), 200, nil)
	requireStatus(t, f.send(t, "DELETE", reservationPath, "", nil), 428, nil)
	requireStatus(t, f.send(t, "DELETE", reservationPath, "", map[string]string{"If-Match": version}), 204, nil)
	requireStatus(t, f.send(t, "DELETE", reservationPath, "", map[string]string{"If-Match": version}), 404, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/reservations?status=cancelled", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].EndedAt == nil || history.Items[0].Status != "cancelled" {
		t.Fatal("owned cancellation did not retain its lifecycle")
	}
	requireStatus(t, visitor.send(t, "GET", sharedPath+"/wishes/"+wish.Id+"/reservations/current", "", headers), 200, &reservation)
	if reservation.Quantity != 2 {
		t.Fatal("owned cancellation changed another participant's reservation")
	}
}

func TestOwnedReservation_WhenListIsPrivate_CreatesWithoutAnyShareCapability(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "private-reservation-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Private reservation","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Private gift"}`, nil), 201, &wish)
	// Act
	var reservation domain.GiftReservationResponse
	path := "/api/v1/wishlists/" + list.Id + "/wishes/" + wish.Id + "/reservations/current"
	requireStatus(t, f.send(t, "PUT", path, `{"quantity":1}`, nil), 201, &reservation)
	// Assert
	requireStatus(t, f.send(t, "GET", path, "", nil), 200, &reservation)
	var links domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 404, &links)
}
