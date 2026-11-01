//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestWishlistSubscriptions_WhenCapabilityChanges_RevokesSummariesWithoutChangingParticipation(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "subscription-owner@example.test")
	var list domain.WishlistResponse
	createdList := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Followed list","occasion":"other"}`, nil)
	listVersion := createdList.Header.Get("ETag")
	requireStatus(t, createdList, 201, &list)
	ownerPath := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", ownerPath+"/wishes", `{"name":"Private wish content","note":"Not a subscription summary"}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	createdLink := f.send(t, "POST", ownerPath+"/share-link", "", nil)
	linkVersion := createdLink.Header.Get("ETag")
	requireStatus(t, createdLink, 201, &link)
	shareURL, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": shareURL.Fragment}
	base := "/api/v1/shared-wishlists/" + link.Id
	createPath := base + "/subscriptions"
	subscriber := f.visitor(t)
	subscriber.member(t, "subscriber@example.test")
	stranger := f.visitor(t)
	stranger.member(t, "subscription-stranger@example.test")
	anonymous := f.visitor(t)
	requireStatus(t, anonymous.send(t, "POST", createPath, "", headers), 401, nil)
	requireStatus(t, f.send(t, "POST", createPath, "", headers), 409, nil)
	requireStatus(t, subscriber.send(t, "POST", createPath, "", nil), 404, nil)
	requireStatus(t, subscriber.send(t, "GET", createPath+"/current", "", headers), 404, nil)
	// Act
	var subscription domain.WishlistSubscriptionResponse
	created := subscriber.send(t, "POST", createPath, "", headers)
	location := created.Header.Get("Location")
	requireStatus(t, created, 201, &subscription)
	// Assert
	if location != "/api/v1/wishlist-subscriptions/"+subscription.Id || subscription.WishlistId != list.Id || subscription.ShareUrl != link.ShareUrl || subscription.Name != list.Name {
		t.Fatal("created subscription contract differs")
	}
	requireStatus(t, subscriber.send(t, "GET", location, "", nil), 200, nil)
	requireStatus(t, subscriber.send(t, "GET", createPath+"/current", "", headers), 200, nil)
	requireStatus(t, subscriber.send(t, "GET", base+"/participants/current", "", headers), 404, nil)
	requireStatus(t, subscriber.send(t, "POST", createPath, "", headers), 409, nil)
	requireStatus(t, stranger.send(t, "GET", location, "", nil), 404, nil)
	requireStatus(t, stranger.send(t, "DELETE", location, "", nil), 404, nil)
	var page domain.PaginatedResponse[domain.WishlistSubscriptionResponse]
	requireStatus(t, subscriber.send(t, "GET", "/api/v1/wishlist-subscriptions", "", nil), 200, &page)
	if page.TotalCount != 1 || len(page.Items) != 1 || page.Items[0].Id != subscription.Id || page.CurrentPage != 1 || page.PageSize != 20 {
		t.Fatal("subscription pagination contract differs")
	}
	requireStatus(t, subscriber.send(t, "GET", "/api/v1/wishlist-subscriptions?page=100", "", nil), 200, &page)
	if page.TotalCount != 1 || len(page.Items) != 0 || page.CurrentPage != 100 {
		t.Fatal("out-of-range subscription metadata differs")
	}
	for _, query := range []string{"page=0", "page=", "pageSize=0", "pageSize=101", "pageSize=x"} {
		requireStatus(t, subscriber.send(t, "GET", "/api/v1/wishlist-subscriptions?"+query, "", nil), 400, nil)
	}
	archived := f.send(t, "PATCH", ownerPath, `{"isArchived":true}`, map[string]string{"If-Match": listVersion})
	listVersion = archived.Header.Get("ETag")
	requireStatus(t, archived, 200, nil)
	requireStatus(t, subscriber.send(t, "GET", location, "", nil), 404, nil)
	requireStatus(t, subscriber.send(t, "GET", "/api/v1/wishlist-subscriptions", "", nil), 200, &page)
	if page.TotalCount != 0 || page.Items == nil || len(page.Items) != 0 || page.TotalPages != 0 || page.HasPreviousPage || page.HasNextPage {
		t.Fatal("archive disclosed an inaccessible subscription")
	}
	requireStatus(t, subscriber.send(t, "GET", createPath+"/current", "", headers), 404, nil)
	requireStatus(t, f.send(t, "PATCH", ownerPath, `{"isArchived":false}`, map[string]string{"If-Match": listVersion}), 200, nil)
	requireStatus(t, subscriber.send(t, "GET", location, "", nil), 200, nil)
	rotated := f.send(t, "PUT", ownerPath+"/share-link", "", map[string]string{"If-Match": linkVersion})
	linkVersion = rotated.Header.Get("ETag")
	requireStatus(t, rotated, 200, &link)
	requireStatus(t, subscriber.send(t, "GET", location, "", nil), 404, nil)
	requireStatus(t, subscriber.send(t, "GET", createPath+"/current", "", headers), 404, nil)
	shareURL, err = url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers["X-MonKado-Share-Token"] = shareURL.Fragment
	requireStatus(t, subscriber.send(t, "POST", createPath, "", headers), 409, nil)
	requireStatus(t, subscriber.send(t, "DELETE", location, "", nil), 204, nil)
	requireStatus(t, subscriber.send(t, "DELETE", location, "", nil), 404, nil)
	requireStatus(t, subscriber.send(t, "POST", createPath, "", headers), 201, &subscription)
	location = "/api/v1/wishlist-subscriptions/" + subscription.Id
	requireStatus(t, subscriber.send(t, "POST", base+"/participants", "", headers), 201, nil)
	reservationPath := base + "/wishes/" + wish.Id + "/reservations/current"
	requireStatus(t, subscriber.send(t, "PUT", reservationPath, `{"quantity":1}`, headers), 201, nil)
	requireStatus(t, subscriber.send(t, "DELETE", location, "", nil), 204, nil)
	requireStatus(t, subscriber.send(t, "GET", reservationPath, "", headers), 200, nil)
	requireStatus(t, subscriber.send(t, "POST", createPath, "", headers), 201, &subscription)
	location = "/api/v1/wishlist-subscriptions/" + subscription.Id
	requireStatus(t, f.send(t, "DELETE", ownerPath+"/share-link", "", map[string]string{"If-Match": linkVersion}), 204, nil)
	requireStatus(t, subscriber.send(t, "GET", location, "", nil), 404, nil)
	requireStatus(t, subscriber.send(t, "GET", "/api/v1/wishlist-subscriptions", "", nil), 200, &page)
	if page.TotalCount != 0 || len(page.Items) != 0 {
		t.Fatal("deleted capability retained an accessible subscription")
	}
}
