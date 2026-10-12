//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestWishUpdates_WhenQuantityFallsBelowReservations_RollsBackAndPreservesCurrentVersion(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "editowner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Editable gifts","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	created := f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Original gift","quantity":3,"price":12.34}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	path := "/api/v1/wishlists/" + list.Id + "/wishes/" + wish.Id
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	share, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	visitor := f.visitor(t)
	headers := map[string]string{"X-MonKado-Share-Token": share.Fragment}
	base := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, visitor.send(t, "POST", base+"/participants", `{"displayName":"Gift giver"}`, headers), 201, nil)
	reserved := visitor.send(t, "PUT", base+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":2}`, headers)
	reservationVersion := reserved.Header.Get("ETag")
	requireStatus(t, reserved, 201, nil)
	// Act
	var failure domain.ErrorResponse
	requireStatus(t, f.send(t, "PUT", path, `{"name":"Must roll back","quantity":1,"price":99.99}`, map[string]string{"If-Match": etag}), 409, &failure)
	// Assert
	if failure.ErrorCode == nil || *failure.ErrorCode != domain.WishQuantityBelowReserved {
		t.Fatal("reserved-quantity conflict code differs")
	}
	read := f.send(t, "GET", path, "", nil)
	if read.Header.Get("ETag") != etag {
		t.Fatal("failed mutation changed the wish version")
	}
	requireStatus(t, read, 200, &wish)
	if wish.Name != "Original gift" || wish.Quantity != 3 || wish.Price == nil || *wish.Price != "12.34" || wish.UpdatedAt != nil {
		t.Fatal("failed mutation persisted part of its changes")
	}
	updated := f.send(t, "PUT", path, `{"name":" Updated gift ","quantity":2,"price":15.90,"note":"A note\nfor a gift","url":"https://merchant.example/item"}`, map[string]string{"If-Match": etag})
	next := updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &wish)
	if next == etag || wish.Name != "Updated gift" || wish.Quantity != 2 || wish.Price == nil || *wish.Price != "15.90" || wish.UpdatedAt == nil || wish.Note == nil {
		t.Fatal("successful update contract differs")
	}
	requireStatus(t, f.send(t, "PUT", path, `{"name":"Stale edit","quantity":2}`, map[string]string{"If-Match": etag}), 412, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+security.NewID()+"/wishes/"+wish.Id, "", nil), 404, nil)
	requireStatus(t, f.send(t, "PUT", "/api/v1/wishlists/"+list.Id+"/wishes/"+security.NewID(), `{"name":"Missing wish","quantity":2}`, map[string]string{"If-Match": next}), 404, nil)
	var available domain.SharedWishlistResponse
	requireStatus(t, visitor.send(t, "GET", base+"?availableOnly=TRUE", "", headers), 200, &available)
	if available.Wishes == nil || len(available.Wishes) != 1 || available.Wishes[0].CurrentParticipantReservedQuantity == nil || *available.Wishes[0].CurrentParticipantReservedQuantity != 2 {
		t.Fatal("available-only mode hid the participant's own reserved gift")
	}
	requireStatus(t, visitor.send(t, "GET", base+"?availableOnly=1", "", headers), 400, nil)
	requireStatus(t, visitor.send(t, "GET", base+"?availableOnly=true&availableOnly=false", "", headers), 400, nil)
	headers["If-Match"] = reservationVersion
	changed := visitor.send(t, "PUT", base+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, headers)
	headers["If-Match"] = changed.Header.Get("ETag")
	var reservation domain.GiftReservationResponse
	requireStatus(t, changed, 200, &reservation)
	if reservation.Quantity != 1 || reservation.UpdatedAt == nil {
		t.Fatal("absolute reservation update contract differs")
	}
	requireStatus(t, visitor.send(t, "GET", base+"/wishes/"+wish.Id+"/reservations/current", "", headers), 200, &reservation)
}
