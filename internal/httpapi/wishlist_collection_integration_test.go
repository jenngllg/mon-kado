//go:build integration

package httpapi_test

import (
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestWishlistCollection_WhenDifferentMembersOwnLists_ReturnsOnlyOwnedListsInCreationOrder(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC().Truncate(time.Microsecond)
	f.service.Now = func() time.Time { return now }
	f.member(t, "first-owner@example.test")
	firstAccess := f.access
	var lists []domain.WishlistResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &lists)
	if lists == nil || len(lists) != 0 {
		t.Fatal("empty collection must serialize as an array")
	}
	var first, second domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"First list","occasion":"birthday"}`, nil), 201, &first)
	now = now.Add(time.Second)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Second list","occasion":"christmas"}`, nil), 201, &second)
	f.member(t, "second-owner@example.test")
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Private other list","occasion":"other"}`, nil), 201, nil)
	// Act
	f.access = firstAccess
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &lists)
	// Assert
	if len(lists) != 2 || lists[0].Id != second.Id || lists[1].Id != first.Id {
		t.Fatal("collection order or ownership isolation differs")
	}
	if lists[0].UpdatedAt != nil || lists[0].Message != nil {
		t.Fatal("nullable creation fields differ")
	}
}
