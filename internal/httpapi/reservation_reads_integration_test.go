//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestReservationReads_WhenRepeated_DoNotConsumeTheMutationBudget(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "reservation-read-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Read reservations","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	base := "/api/v1/wishlists/" + list.Id
	requireStatus(t, f.send(t, "POST", base+"/wishes", `{"name":"Gift","quantity":2}`, nil), 201, &wish)
	owned := base + "/wishes/" + wish.Id + "/reservations/current"
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", base+"/share-link", "", nil), 201, &link)
	u, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": u.Fragment}
	visitor := f.visitor(t)
	sharedBase := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, visitor.send(t, "POST", sharedBase+"/participants", `{"displayName":"Reader"}`, headers), 201, nil)
	shared := sharedBase + "/wishes/" + wish.Id + "/reservations/current"
	// Act: exceed the mutation allowance using reads, while remaining below shared/perimeter limits.
	for range 25 {
		requireStatus(t, f.send(t, "GET", owned, "", nil), 404, nil)
		requireStatus(t, visitor.send(t, "GET", shared, "", headers), 404, nil)
	}
	// Assert
	requireStatus(t, f.send(t, "PUT", owned, `{"quantity":1}`, nil), 201, nil)
	requireStatus(t, visitor.send(t, "PUT", shared, `{"quantity":1}`, headers), 201, nil)
	requireStatus(t, f.send(t, "GET", owned, "", nil), 200, nil)
	requireStatus(t, visitor.send(t, "GET", shared, "", headers), 200, nil)
}
