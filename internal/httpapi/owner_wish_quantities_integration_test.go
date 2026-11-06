//go:build integration

package httpapi_test

import (
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestOwnerWishQuantities_WhenSurpriseModeChanges_ReturnsTotalsOnlyWhenVisible(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner-quantities@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Visible quantities","occasion":"other","surpriseMode":false}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", base+"/wishes", `{"name":"Wish","quantity":3}`, nil), 201, &wish)
	if wish.ReservedQuantity == nil || *wish.ReservedQuantity != 0 || wish.AvailableQuantity == nil || *wish.AvailableQuantity != 3 {
		t.Fatal("creation omitted initial visible quantities")
	}
	wishPath := base + "/wishes/" + wish.Id
	requireStatus(t, f.send(t, "PUT", wishPath+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
	for _, surprise := range []bool{false, true} {
		// Act
		if surprise {
			current := f.send(t, "GET", base, "", nil)
			version := current.Header.Get("ETag")
			requireStatus(t, current, 200, nil)
			requireStatus(t, f.send(t, "PUT", base, `{"name":"Visible quantities","occasion":"other","surpriseMode":true}`, map[string]string{"If-Match": version}), 200, nil)
		}
		requireStatus(t, f.send(t, "GET", wishPath, "", nil), 200, &wish)
		var collection domain.WishCollectionResponse
		requireStatus(t, f.send(t, "GET", base+"/wishes", "", nil), 200, &collection)
		if len(collection.Wishes) != 1 {
			t.Fatal("wish collection lost its item")
		}
		// Assert
		for _, quantities := range [][2]*int{{wish.ReservedQuantity, wish.AvailableQuantity}, {collection.Wishes[0].ReservedQuantity, collection.Wishes[0].AvailableQuantity}} {
			if surprise {
				if quantities[0] != nil || quantities[1] != nil {
					t.Fatal("surprise mode disclosed reservation totals")
				}
			} else if quantities[0] == nil || *quantities[0] != 1 || quantities[1] == nil || *quantities[1] != 2 {
				t.Fatal("visible reservation totals are incorrect")
			}
		}
	}
}
