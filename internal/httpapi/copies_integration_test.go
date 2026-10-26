//go:build integration

package httpapi_test

import (
	"bytes"
	"io"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestWishCopies_WhenOwnedOrShared_CopiesContentAndIndependentImagesWithoutReservations(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "copy-owner@example.test")
	var source, destination domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Source","occasion":"other","surpriseMode":false}`, nil), 201, &source)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Destination","occasion":"other","surpriseMode":false}`, nil), 201, &destination)
	var original domain.WishResponse
	created := f.send(t, "POST", "/api/v1/wishlists/"+source.Id+"/wishes", `{"name":"Camera","note":"Private note","color":"Coral","size":"Medium","price":"12.30","quantity":3,"isFavorite":true,"isSurprise":true,"acceptsSecondHand":true}`, nil)
	version := created.Header.Get("ETag")
	requireStatus(t, created, 201, &original)
	sourcePath := "/api/v1/wishlists/" + source.Id + "/wishes/" + original.Id
	updated := f.upload(t, sourcePath+"/image", version, 99)
	version = updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &original)
	requireStatus(t, f.send(t, "PUT", sourcePath+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
	// Act
	var copied domain.WishResponse
	copyResponse := f.send(t, "POST", sourcePath+"/copies", `{"destinationWishlistId":"`+destination.Id+`"}`, nil)
	copyVersion := copyResponse.Header.Get("ETag")
	requireStatus(t, copyResponse, 201, &copied)
	// Assert
	if copied.Id == original.Id || copied.WishlistId != destination.Id || copied.Name != original.Name || copied.Price == nil || *copied.Price != "12.30" || copied.Quantity != 3 || copied.IsFavorite || copied.ReservedQuantity == nil || *copied.ReservedQuantity != 0 || copied.UpdatedAt != nil || copied.ImageUrl == nil || copyVersion == "" || copyResponse.Header.Get("Location") != "/api/v1/wishlists/"+destination.Id+"/wishes/"+copied.Id {
		t.Fatal("independent copy contract differs")
	}
	if copied.Color == nil || *copied.Color != "Coral" || copied.Size == nil || *copied.Size != "Medium" || !copied.IsSurprise || !copied.AcceptsSecondHand {
		t.Fatal("owned copy lost the source variants")
	}
	readImage := func(address string) []byte {
		t.Helper()
		u, err := url.Parse(address)
		if err != nil {
			t.Fatal(err)
		}
		r := f.send(t, "GET", u.RequestURI(), "", nil)
		data, err := io.ReadAll(r.Body)
		r.Body.Close()
		if err != nil || r.StatusCode != 200 {
			t.Fatal("copied image unavailable")
		}
		return data
	}
	if !bytes.Equal(readImage(*original.ImageUrl), readImage(*copied.ImageUrl)) {
		t.Fatal("normalized bytes changed during copy")
	}
	requireStatus(t, f.send(t, "DELETE", sourcePath+"/image", "", map[string]string{"If-Match": version}), 204, nil)
	readImage(*copied.ImageUrl)
	for _, invalid := range []string{`{}`, `{"destinationWishlistId":null}`, `{"destinationWishlistId":"invalid"}`, `{"destinationWishlistId":"00000000-0000-0000-0000-000000000000"}`} {
		requireStatus(t, f.send(t, "POST", sourcePath+"/copies", invalid, nil), 400, nil)
	}
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+source.Id+"/share-link", "", nil), 201, &link)
	u, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	visitor := f.visitor(t)
	visitor.member(t, "copy-reader@example.test")
	var visitorList domain.WishlistResponse
	requireStatus(t, visitor.send(t, "POST", "/api/v1/wishlists", `{"name":"Reader list","occasion":"other"}`, nil), 201, &visitorList)
	path := "/api/v1/wishlists/" + visitorList.Id + "/wishes/copies"
	body := `{"sourceShareLinkId":"` + link.Id + `","sourceWishId":"` + original.Id + `"}`
	requireStatus(t, visitor.send(t, "POST", path, body, nil), 404, nil)
	requireStatus(t, visitor.send(t, "POST", path, body, map[string]string{"X-MonKado-Share-Token": u.Fragment}), 201, &copied)
	if copied.Name != original.Name || copied.IsFavorite || copied.ImageUrl != nil {
		t.Fatal("shared copy differs")
	}
	if copied.Color == nil || *copied.Color != "Coral" || copied.Size == nil || *copied.Size != "Medium" || !copied.IsSurprise || !copied.AcceptsSecondHand {
		t.Fatal("shared copy lost the source variants")
	}
	requireStatus(t, visitor.send(t, "POST", sourcePath+"/copies", `{"destinationWishlistId":"`+visitorList.Id+`"}`, nil), 404, nil)
	listRead := f.send(t, "GET", "/api/v1/wishlists/"+source.Id, "", nil)
	listVersion := listRead.Header.Get("ETag")
	requireStatus(t, listRead, 200, nil)
	requireStatus(t, f.send(t, "PATCH", "/api/v1/wishlists/"+source.Id, `{"isArchived":true}`, map[string]string{"If-Match": listVersion}), 200, nil)
	requireStatus(t, f.send(t, "POST", sourcePath+"/copies", `{"destinationWishlistId":"`+destination.Id+`"}`, nil), 409, nil)
	requireStatus(t, visitor.send(t, "POST", path, body, map[string]string{"X-MonKado-Share-Token": u.Fragment}), 404, nil)
}
