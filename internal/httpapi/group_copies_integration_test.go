//go:build integration

package httpapi_test

import (
	"bytes"
	"io"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestGroupWishCopies_WhenExplicitlyCopying_CreatesIndependentContentWithoutPublicAccessOrReservations(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	s.invite(t, s.foreignID)
	var source, destination domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Private source","occasion":"other","surpriseMode":false}`, nil), 201, &source)
	destinationResponse := s.member.send(t, "POST", "/api/v1/wishlists", `{"name":"My destination","occasion":"other","surpriseMode":false}`, nil)
	destinationVersion := destinationResponse.Header.Get("ETag")
	requireStatus(t, destinationResponse, 201, &destination)
	groupList := s.base + "/wishlists/" + source.Id
	requireStatus(t, s.owner.send(t, "PUT", groupList, "", nil), 201, nil)
	ownedSource := "/api/v1/wishlists/" + source.Id
	var original domain.WishResponse
	created := s.owner.send(t, "POST", ownedSource+"/wishes", `{"name":"A camera","note":"Blue please","color":"Blue","size":"Small","price":"12.30","quantity":3,"isFavorite":true,"isSurprise":true,"acceptsSecondHand":true}`, nil)
	version := created.Header.Get("ETag")
	requireStatus(t, created, 201, &original)
	sourceWish := ownedSource + "/wishes/" + original.Id
	uploaded := s.owner.upload(t, sourceWish+"/image", version, 99)
	version = uploaded.Header.Get("ETag")
	requireStatus(t, uploaded, 200, &original)
	requireStatus(t, s.owner.send(t, "PUT", sourceWish+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
	path := groupList + "/wishes/" + original.Id + "/copies"
	body := `{"destinationWishlistId":"` + destination.Id + `"}`
	anonymous := s.owner.visitor(t)
	requireStatus(t, anonymous.send(t, "POST", path, body, nil), 401, nil)
	requireStatus(t, s.foreign.send(t, "POST", path, body, nil), 404, nil)
	requireStatus(t, s.member.send(t, "POST", groupList+"/wishes/"+security.NewID()+"/copies", body, nil), 404, nil)
	requireStatus(t, s.member.send(t, "POST", path, `{"destinationWishlistId":"`+source.Id+`"}`, nil), 404, nil)
	for _, invalid := range []string{`{}`, `{"destinationWishlistId":null}`, `{"destinationWishlistId":"bad"}`} {
		requireStatus(t, s.member.send(t, "POST", path, invalid, nil), 400, nil)
	}
	// Act
	var copied domain.WishResponse
	response := s.member.send(t, "POST", path, body, nil)
	location, copiedVersion := response.Header.Get("Location"), response.Header.Get("ETag")
	requireStatus(t, response, 201, &copied)
	// Assert
	if copied.Id == original.Id || copied.WishlistId != destination.Id || copied.Name != original.Name || copied.Note == nil || *copied.Note != "Blue please" || copied.Price == nil || *copied.Price != "12.30" || copied.Quantity != 3 || copied.Color == nil || *copied.Color != "Blue" || copied.Size == nil || *copied.Size != "Small" || !copied.IsSurprise || !copied.AcceptsSecondHand || copied.IsFavorite || copied.ReservedQuantity == nil || *copied.ReservedQuantity != 0 || copied.UpdatedAt != nil || copied.ImageUrl == nil || copiedVersion == "" || location != "/api/v1/wishlists/"+destination.Id+"/wishes/"+copied.Id {
		t.Fatal("group copy is not independent or lost source content")
	}
	readImage := func(f *fixture, address string) []byte {
		t.Helper()
		u, err := url.Parse(address)
		if err != nil {
			t.Fatal(err)
		}
		r := f.send(t, "GET", u.RequestURI(), "", nil)
		data, err := io.ReadAll(r.Body)
		r.Body.Close()
		if err != nil || r.StatusCode != 200 {
			t.Fatal("copied image is unavailable")
		}
		return data
	}
	if !bytes.Equal(readImage(s.owner, *original.ImageUrl), readImage(s.member, *copied.ImageUrl)) {
		t.Fatal("group copy changed normalized image bytes")
	}
	requireStatus(t, s.owner.send(t, "DELETE", sourceWish+"/image", "", map[string]string{"If-Match": version}), 204, nil)
	readImage(s.member, *copied.ImageUrl)
	requireStatus(t, s.owner.send(t, "GET", ownedSource+"/share-link", "", nil), 404, nil)
	var groupRead domain.SharedWishlistResponse
	requireStatus(t, s.member.send(t, "GET", groupList, "", nil), 200, &groupRead)
	if groupRead.CurrentParticipant != nil || len(groupRead.Wishes) != 1 {
		t.Fatal("copying participated or changed source wishes")
	}
	requireStatus(t, s.member.send(t, "GET", groupList+"/wishes/"+original.Id+"/reservations/current", "", nil), 404, nil)
	var followed domain.PaginatedResponse[domain.WishlistSubscriptionResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlist-subscriptions", "", nil), 200, &followed)
	if followed.TotalCount != 0 {
		t.Fatal("copying followed the source list")
	}
	// A destination that changed while the picker was open must still reject writes.
	requireStatus(t, s.member.send(t, "PATCH", "/api/v1/wishlists/"+destination.Id, `{"isArchived":true}`, map[string]string{"If-Match": destinationVersion}), 200, nil)
	requireStatus(t, s.member.send(t, "POST", path, body, nil), 409, nil)
}

func TestGroupWishCopies_WhenSourceAccessRevoked_DoesNotWriteToDestination(t *testing.T) {
	for _, change := range []string{"unshared", "left", "archived", "foreignChild"} {
		t.Run(change, func(t *testing.T) {
			// Arrange
			s := newGroupScenario(t)
			s.accept(t, s.member, s.memberID)
			var source, destination domain.WishlistResponse
			created := s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Source","occasion":"other"}`, nil)
			version := created.Header.Get("ETag")
			requireStatus(t, created, 201, &source)
			requireStatus(t, s.member.send(t, "POST", "/api/v1/wishlists", `{"name":"Destination","occasion":"other"}`, nil), 201, &destination)
			var wish domain.WishResponse
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+source.Id+"/wishes", `{"name":"Original"}`, nil), 201, &wish)
			base := s.base + "/wishlists/" + source.Id
			requireStatus(t, s.owner.send(t, "PUT", base, "", nil), 201, nil)
			switch change {
			case "unshared":
				requireStatus(t, s.owner.send(t, "DELETE", base, "", nil), 204, nil)
			case "left":
				requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
			case "archived":
				requireStatus(t, s.owner.send(t, "PATCH", "/api/v1/wishlists/"+source.Id, `{"isArchived":true}`, map[string]string{"If-Match": version}), 200, nil)
			case "foreignChild":
				var other domain.WishlistResponse
				requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Unshared","occasion":"other"}`, nil), 201, &other)
				requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+other.Id+"/wishes", `{"name":"Foreign child"}`, nil), 201, &wish)
			}
			// Act
			requireStatus(t, s.member.send(t, "POST", base+"/wishes/"+wish.Id+"/copies", `{"destinationWishlistId":"`+destination.Id+`"}`, nil), 404, nil)
			// Assert
			var collection domain.WishCollectionResponse
			requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlists/"+destination.Id+"/wishes", "", nil), 200, &collection)
			if len(collection.Wishes) != 0 {
				t.Fatal("revoked source wrote to the destination")
			}
		})
	}
}
