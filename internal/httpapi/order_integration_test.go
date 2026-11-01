//go:build integration

package httpapi_test

import (
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestReorder_WhenPositionsHaveGaps_PreservesPositionsAndNoOpVersions(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "order@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Order","occasion":"other"}`, nil), 201, &list)
	path := "/api/v1/wishlists/" + list.Id + "/wishes"
	var first, removed, last domain.WishResponse
	requireStatus(t, f.send(t, "POST", path, `{"name":"First"}`, nil), 201, &first)
	created := f.send(t, "POST", path, `{"name":"Removed"}`, nil)
	deletedETag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &removed)
	requireStatus(t, f.send(t, "POST", path, `{"name":"Last"}`, nil), 201, &last)
	requireStatus(t, f.send(t, "DELETE", path+"/"+removed.Id, "", map[string]string{"If-Match": deletedETag}), 204, nil)
	collection := f.send(t, "GET", path, "", nil)
	version := collection.Header.Get("ETag")
	requireStatus(t, collection, 200, nil)
	order := jsonInput(t, domain.ReorderWishesRequest{WishIds: []string{last.Id, first.Id}})
	// Act
	changed := f.send(t, "PATCH", path, order, map[string]string{"If-Match": version})
	changedVersion := changed.Header.Get("ETag")
	var result domain.WishOrderResponse
	requireStatus(t, changed, 200, &result)
	unchanged := f.send(t, "PATCH", path, order, map[string]string{"If-Match": changedVersion})
	unchangedVersion := unchanged.Header.Get("ETag")
	var repeated domain.WishOrderResponse
	requireStatus(t, unchanged, 200, &repeated)
	// Assert
	if result.Wishes[0].Position != first.Position || result.Wishes[1].Position != last.Position || repeated.Wishes[0] != result.Wishes[0] || repeated.Wishes[1] != result.Wishes[1] || unchangedVersion != changedVersion {
		t.Fatal("reordering changed existing position values or no-op versions")
	}
	requireStatus(t, f.send(t, "PATCH", path, order, map[string]string{"If-Match": version}), 412, nil)
	requireStatus(t, f.send(t, "PATCH", path, jsonInput(t, domain.ReorderWishesRequest{WishIds: []string{first.Id}}), map[string]string{"If-Match": changedVersion}), 409, nil)
	var empty domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Empty","occasion":"other"}`, nil), 201, &empty)
	emptyPath := "/api/v1/wishlists/" + empty.Id + "/wishes"
	emptyCollection := f.send(t, "GET", emptyPath, "", nil)
	emptyVersion := emptyCollection.Header.Get("ETag")
	requireStatus(t, emptyCollection, 200, nil)
	requireStatus(t, f.send(t, "PATCH", emptyPath, `{"wishIds":[]}`, map[string]string{"If-Match": emptyVersion}), 200, &result)
	if result.Wishes == nil || len(result.Wishes) != 0 {
		t.Fatal("empty order must preserve an empty JSON array")
	}
}
