//go:build integration

package httpapi_test

import (
	"io"
	"log/slog"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func TestWishCopies_WhenOwnedSourceHasImage_CreatesIndependentWishAndRetainsReferencedImage(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "copy-owner@example.test")
	var source, destination domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Source","occasion":"other"}`, nil), 201, &source)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Destination","occasion":"other"}`, nil), 201, &destination)
	var original domain.WishResponse
	sourcePath := "/api/v1/wishlists/" + source.Id + "/wishes"
	created := f.send(t, "POST", sourcePath, `{"name":"Copy me","note":"Retain this","price":12.34,"quantity":3,"isFavorite":true,"color":"Blue","size":"M","acceptsSecondHand":true}`, nil)
	version := created.Header.Get("ETag")
	requireStatus(t, created, 201, &original)
	sourcePath += "/" + original.Id
	upload := f.upload(t, sourcePath+"/image", version, 42)
	version = upload.Header.Get("ETag")
	requireStatus(t, upload, 200, &original)
	// Act
	var copied domain.WishResponse
	response := f.send(t, "POST", sourcePath+"/copies", jsonInput(t, map[string]string{"destinationWishlistId": destination.Id}), nil)
	location, copyVersion := response.Header.Get("Location"), response.Header.Get("ETag")
	requireStatus(t, response, 201, &copied)
	// Assert
	if copied.Id == original.Id || copied.WishlistId != destination.Id || copied.Name != original.Name || copied.Quantity != 3 || copied.IsFavorite || copied.UpdatedAt != nil || copied.ImageUrl == nil || copyVersion == "" || copied.AcceptsSecondHand != original.AcceptsSecondHand || location != "/api/v1/wishlists/"+destination.Id+"/wishes/"+copied.Id {
		t.Fatal("copy lost content, independence or creation metadata")
	}
	requireStatus(t, f.send(t, "GET", location, "", nil), 200, &copied)
	requireStatus(t, f.send(t, "DELETE", sourcePath, "", map[string]string{"If-Match": version}), 204, nil)
	w := &worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	defer w.Close()
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	image, err := url.Parse(*copied.ImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", image.RequestURI(), "", nil), 200, nil)
	requireStatus(t, f.send(t, "GET", sourcePath, "", nil), 404, nil)
}

func TestWishCopies_WhenSourceIsPublicOrGroupScoped_RequiresCurrentAuthorizationWithoutParticipation(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var source, destination domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Source","occasion":"other"}`, nil), 201, &source)
	requireStatus(t, s.member.send(t, "POST", "/api/v1/wishlists", `{"name":"Destination","occasion":"other"}`, nil), 201, &destination)
	var wish domain.WishResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+source.Id+"/wishes", `{"name":"Scoped source","quantity":2}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+source.Id+"/share-link", "", nil), 201, &link)
	share, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": share.Fragment}
	publicPath := "/api/v1/wishlists/" + destination.Id + "/wishes/copies"
	payload := jsonInput(t, map[string]string{"sourceShareLinkId": link.Id, "sourceWishId": wish.Id})
	// Act / Assert
	requireStatus(t, s.member.send(t, "POST", publicPath, payload, nil), 404, nil)
	var copied domain.WishResponse
	requireStatus(t, s.member.send(t, "POST", publicPath, payload, headers), 201, &copied)
	groupPath := s.base + "/wishlists/" + source.Id + "/wishes/" + wish.Id + "/copies"
	body := jsonInput(t, map[string]string{"destinationWishlistId": destination.Id})
	requireStatus(t, s.member.send(t, "POST", groupPath, body, nil), 201, &copied)
	requireStatus(t, s.foreign.send(t, "POST", groupPath, body, nil), 404, nil)
	var shared domain.SharedWishlistResponse
	requireStatus(t, s.member.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id, "", headers), 200, &shared)
	if shared.CurrentParticipant != nil {
		t.Fatal("copying unexpectedly joined or reserved the source")
	}
	current := s.owner.send(t, "GET", "/api/v1/wishlists/"+source.Id+"/share-link", "", nil)
	tag := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	requireStatus(t, s.owner.send(t, "DELETE", "/api/v1/wishlists/"+source.Id+"/share-link", "", map[string]string{"If-Match": tag}), 204, nil)
	requireStatus(t, s.member.send(t, "POST", publicPath, payload, headers), 404, nil)
	var collection domain.WishCollectionResponse
	requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlists/"+destination.Id+"/wishes", "", nil), 200, &collection)
	if len(collection.Wishes) != 2 {
		t.Fatal("failed copy mutated the destination")
	}
}

func TestWishCopies_WhenInputOrScopeIsInvalid_RejectsWithoutMutatingDestination(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "copy-validation@example.test")
	var source, destination domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Source","occasion":"other"}`, nil), 201, &source)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Destination","occasion":"other"}`, nil), 201, &destination)
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+source.Id+"/wishes", `{"name":"Source wish"}`, nil), 201, &wish)
	path := "/api/v1/wishlists/" + source.Id + "/wishes/" + wish.Id + "/copies"
	for _, tc := range []struct {
		name, body string
		status     int
	}{
		{"missing", `{}`, 400}, {"malformed", `{"destinationWishlistId":"invalid"}`, 400}, {"same", jsonInput(t, map[string]string{"destinationWishlistId": source.Id}), 409}, {"missingDestination", jsonInput(t, map[string]string{"destinationWishlistId": security.NewID()}), 404},
	} {
		t.Run(tc.name, func(t *testing.T) { requireStatus(t, f.send(t, "POST", path, tc.body, nil), tc.status, nil) })
	}
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+source.Id+"/wishes/"+security.NewID()+"/copies", jsonInput(t, map[string]string{"destinationWishlistId": destination.Id}), nil), 404, nil)
	current := f.send(t, "GET", "/api/v1/wishlists/"+destination.Id, "", nil)
	tag := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	requireStatus(t, f.send(t, "PATCH", "/api/v1/wishlists/"+destination.Id, `{"isArchived":true}`, map[string]string{"If-Match": tag}), 200, nil)
	requireStatus(t, f.send(t, "POST", path, jsonInput(t, map[string]string{"destinationWishlistId": destination.Id}), nil), 409, nil)
	var collection domain.WishCollectionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+destination.Id+"/wishes", "", nil), 200, &collection)
	if len(collection.Wishes) != 0 {
		t.Fatal("invalid copies changed the destination")
	}
}

func TestWishCopies_WhenDestinationIsFollowed_NotifiesOnceOnlyAfterSuccessfulCopy(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	var source domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Copy source","occasion":"other"}`, nil), 201, &source)
	var wish domain.WishResponse
	sourcePath := "/api/v1/wishlists/" + source.Id + "/wishes"
	requireStatus(t, s.owner.send(t, "POST", sourcePath, `{"name":"Copied entry"}`, nil), 201, &wish)
	if len(notificationPage(t, s.member)) != 0 {
		t.Fatal("an unrelated source list notified the destination follower")
	}
	// Act
	copyPath := sourcePath + "/" + wish.Id + "/copies"
	requireStatus(t, s.owner.send(t, "POST", copyPath, jsonInput(t, map[string]string{"destinationWishlistId": s.list.Id}), nil), 201, nil)
	var failure domain.ErrorResponse
	requireStatus(t, s.owner.send(t, "POST", copyPath, jsonInput(t, map[string]string{"destinationWishlistId": source.Id}), nil), 409, &failure)
	// Assert
	if failure.ErrorCode == nil || *failure.ErrorCode != domain.WishCopySameWishlist {
		t.Fatal("same-list copying did not retain its stable business error")
	}
	notifications := notificationPage(t, s.member)
	if len(notifications) != 1 || notifications[0].Kind != "wishesAdded" || notifications[0].Quantity != 1 {
		t.Fatal("copy did not notify its destination follower exactly once")
	}
}
