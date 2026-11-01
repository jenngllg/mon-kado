//go:build integration

package httpapi_test

import (
	"net/url"
	"reflect"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestOwnerReservation_WhenPrivatelyManaged_RetainsReadableVersionedLifecycle(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner-reservation@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Private","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	base := "/api/v1/wishlists/" + list.Id
	requireStatus(t, f.send(t, "POST", base+"/wishes", `{"name":"Wish","quantity":3}`, nil), 201, &wish)
	path := base + "/wishes/" + wish.Id + "/reservations/current"
	// Act / Assert: reads do not create or publish anything.
	var absent domain.ErrorResponse
	requireStatus(t, f.send(t, "GET", path, "", nil), 404, &absent)
	if absent.ErrorCode == nil || *absent.ErrorCode != domain.GiftReservationNotFound {
		t.Fatal("absent personal reservation has the wrong error code")
	}
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if history.TotalCount != 0 {
		t.Fatal("reading created a reservation")
	}
	var saved domain.GiftReservationResponse
	created := f.send(t, "PUT", path, `{"quantity":1}`, nil)
	etag := created.Header.Get("ETag")
	if etag == "" || created.Header.Get("Location") != path {
		t.Fatal("creation is missing resource headers")
	}
	requireStatus(t, created, 201, &saved)
	if saved.Quantity != 1 || saved.WishId != wish.Id || saved.UpdatedAt != nil {
		t.Fatal("incorrect initial personal reservation")
	}
	var current domain.GiftReservationResponse
	read := f.send(t, "GET", path, "", nil)
	if read.Header.Get("ETag") != etag {
		t.Fatal("read changed the reservation version")
	}
	requireStatus(t, read, 200, &current)
	if !reflect.DeepEqual(saved, current) {
		t.Fatal("personal reservation lost on reread")
	}
	requireStatus(t, f.send(t, "PUT", path, `{"quantity":2}`, nil), 428, nil)
	updated := f.send(t, "PUT", path, `{"quantity":2}`, map[string]string{"If-Match": etag})
	next := updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &current)
	if next == etag || current.Quantity != 2 || current.Id != saved.Id || current.UpdatedAt == nil {
		t.Fatal("update lost quantity, identity or version")
	}
	requireStatus(t, f.send(t, "DELETE", path, "", nil), 428, nil)
	requireStatus(t, f.send(t, "DELETE", path, "", map[string]string{"If-Match": etag}), 412, nil)
	requireStatus(t, f.send(t, "DELETE", path, "", map[string]string{"If-Match": next}), 204, nil)
	requireStatus(t, f.send(t, "GET", path, "", nil), 404, nil)
	requireStatus(t, f.send(t, "DELETE", path, "", map[string]string{"If-Match": next}), 404, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/reservations?status=cancelled", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].Id != saved.Id || history.Items[0].Quantity != 2 || history.Items[0].EndedAt == nil {
		t.Fatal("owner cancellation did not retain its lifecycle")
	}
	requireStatus(t, f.send(t, "GET", base+"/share-link", "", nil), 404, nil)
}

func TestOwnerReservation_WhenOthersHaveReserved_ExposesOnlySelfAndEnforcesSharedCapacity(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "private-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Surprise","occasion":"other"}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", base+"/wishes", `{"name":"Wish","quantity":3}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", base+"/share-link", "", nil), 201, &link)
	parsed, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": parsed.Fragment}
	shared := "/api/v1/shared-wishlists/" + link.Id
	visitor := f.visitor(t)
	requireStatus(t, visitor.send(t, "POST", shared+"/participants", `{"displayName":"Guest"}`, headers), 201, nil)
	requireStatus(t, visitor.send(t, "PUT", shared+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":2}`, headers), 201, nil)
	path := base + "/wishes/" + wish.Id + "/reservations/current"
	// Act / Assert
	requireStatus(t, f.send(t, "GET", path, "", nil), 404, nil)
	var reservation map[string]any
	created := f.send(t, "PUT", path, `{"quantity":1}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &reservation)
	requireStatus(t, f.send(t, "GET", path, "", nil), 200, &reservation)
	if len(reservation) != 5 || reservation["quantity"] != float64(1) {
		t.Fatal("owner response exposed other participants or lost personal quantity")
	}
	for _, key := range []string{"id", "wishId", "quantity", "createdAt", "updatedAt"} {
		if _, exists := reservation[key]; !exists {
			t.Fatalf("missing reservation field %s", key)
		}
	}
	var conflict domain.ErrorResponse
	requireStatus(t, f.send(t, "PUT", path, `{"quantity":2}`, map[string]string{"If-Match": etag}), 409, &conflict)
	if conflict.ErrorCode == nil || *conflict.ErrorCode != domain.GiftReservationQuantityUnavailable {
		t.Fatal("shared capacity is not enforced")
	}
	requireStatus(t, f.send(t, "DELETE", path, "", map[string]string{"If-Match": etag}), 204, nil)
	var other domain.GiftReservationResponse
	requireStatus(t, visitor.send(t, "GET", shared+"/wishes/"+wish.Id+"/reservations/current", "", headers), 200, &other)
	if other.Quantity != 2 {
		t.Fatal("owner cancellation changed another participant's reservation")
	}
	// Private self-reservation never authorizes joining the public participant flow as the owner.
	requireStatus(t, f.send(t, "POST", shared+"/participants", "", headers), 409, nil)
}

func TestOwnerReservation_WhenUnauthorizedOrInvalid_RejectsWithoutMutation(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner-access@example.test")
	var list, other domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"One","occasion":"other"}`, nil), 201, &list)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Two","occasion":"other"}`, nil), 201, &other)
	var wish domain.WishResponse
	base := "/api/v1/wishlists/" + list.Id
	requireStatus(t, f.send(t, "POST", base+"/wishes", `{"name":"Wish"}`, nil), 201, &wish)
	path := base + "/wishes/" + wish.Id + "/reservations/current"
	stranger := f.visitor(t)
	// Act / Assert
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		requireStatus(t, stranger.send(t, method, path, `{"quantity":1}`, nil), 401, nil)
	}
	stranger.member(t, "reservation-stranger@example.test")
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		for _, invalid := range []string{path, "/api/v1/wishlists/" + other.Id + "/wishes/" + wish.Id + "/reservations/current"} {
			requireStatus(t, stranger.send(t, method, invalid, `{"quantity":1}`, map[string]string{"If-Match": `"00000001"`}), 404, nil)
		}
	}
	for _, invalid := range []string{"{}", `{"quantity":0}`, `{"quantity":101}`, `{"quantity":1.5}`, `{"quantity":"1"}`} {
		requireStatus(t, f.send(t, "PUT", path, invalid, nil), 400, nil)
	}
	requireStatus(t, f.send(t, "PUT", path, `{"quantity":1}`, map[string]string{"If-Match": "bad"}), 400, nil)
	requireStatus(t, f.send(t, "PUT", path, `{"quantity":1}`, map[string]string{"If-Match": `"00000001"`}), 412, nil)
	requireStatus(t, f.send(t, "PUT", "/api/v1/wishlists/"+other.Id+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, nil), 404, nil)
	requireStatus(t, f.send(t, "PUT", base+"/wishes/"+security.NewID()+"/reservations/current", `{"quantity":1}`, nil), 404, nil)
	requireStatus(t, f.send(t, "GET", path, "", nil), 404, nil)
}
