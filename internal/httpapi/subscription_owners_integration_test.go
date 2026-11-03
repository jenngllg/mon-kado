//go:build integration

package httpapi_test

import (
	"context"
	"errors"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestSubscriptionOwners_WhenFollowing_ReturnsOnlyLiveAuthorizedOwnersWithoutChangingTheExistingContract(t *testing.T) {
	s := newSubscriptionScenario(t)
	const path = "/api/v1/members/current/wishlist-subscription-owners"
	anonymous := s.owner.visitor(t)
	requireStatus(t, anonymous.send(t, "GET", path, "", nil), 401, nil)
	var page domain.PaginatedResponse[domain.WishlistSubscriptionOwnerResponse]
	requireStatus(t, s.member.send(t, "GET", path, "", nil), 200, &page)
	if len(page.Items) != 0 || page.TotalPages != 0 {
		t.Fatal("unexpected owner identities before following")
	}
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	var identity domain.CurrentSessionResponse
	requireStatus(t, s.owner.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &identity)
	requireStatus(t, s.member.send(t, "GET", path+"?pageSize=1", "", nil), 200, &page)
	if len(page.Items) != 1 || page.Items[0].OwnerId != identity.Id || page.Items[0].WishlistId != s.list.Id {
		t.Fatal("incorrect authorized owner identity")
	}
	requireStatus(t, s.owner.send(t, "GET", path, "", nil), 200, &page)
	if len(page.Items) != 0 {
		t.Fatal("foreign subscription identities leaked")
	}
	requireStatus(t, s.member.send(t, "GET", path+"?pageSize=0", "", nil), 400, nil)
	requireStatus(t, s.member.send(t, "GET", path+"?page=2&pageSize=1", "", nil), 200, &page)
	if len(page.Items) != 0 || page.TotalCount != 1 || page.CurrentPage != 2 {
		t.Fatal("out-of-range pagination changed")
	}
	// Revoking the original capability removes its identities too.
	link := s.owner.send(t, "GET", s.base+"/share-link", "", nil)
	tag := link.Header.Get("ETag")
	requireStatus(t, link, 200, nil)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/share-link", "", map[string]string{"If-Match": tag}), 204, nil)
	requireStatus(t, s.member.send(t, "GET", path, "", nil), 200, &page)
	if len(page.Items) != 0 {
		t.Fatal("revoked access retained owner identities")
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	_, err := s.owner.service.SubscriptionOwners(ctx, identity.Id, 1, 20)
	if !errors.Is(err, context.Canceled) {
		t.Fatal("caller cancellation was not preserved")
	}
}
