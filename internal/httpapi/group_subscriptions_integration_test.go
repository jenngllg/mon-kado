//go:build integration

package httpapi_test

import (
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestGroupSubscriptions_WhenFollowingPrivateList_PreservesAccessAndDoesNotParticipate(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Private followed list","occasion":"other"}`, nil), 201, &list)
	owned := "/api/v1/wishlists/" + list.Id
	base := s.base + "/wishlists/" + list.Id
	requireStatus(t, s.owner.send(t, "PUT", base, "", nil), 201, nil)
	var wish domain.WishResponse
	requireStatus(t, s.owner.send(t, "POST", owned+"/wishes", `{"name":"Private wish","quantity":2}`, nil), 201, &wish)
	var shared domain.SharedWishlistResponse
	requireStatus(t, s.member.send(t, "GET", base, "", nil), 200, &shared)
	if !shared.CanSubscribe || shared.CurrentParticipant != nil {
		t.Fatal("group follow is unavailable or reading enrolled the member")
	}
	requireStatus(t, s.owner.send(t, "GET", base, "", nil), 200, &shared)
	if shared.CanSubscribe {
		t.Fatal("owner can follow their own list")
	}
	path := base + "/subscriptions"
	anonymous := s.owner.visitor(t)
	requireStatus(t, anonymous.send(t, "POST", path, "", nil), 401, nil)
	requireStatus(t, s.foreign.send(t, "POST", path, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "GET", path+"/current", "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "POST", path, "", nil), 409, nil)
	requireStatus(t, s.member.send(t, "GET", path+"/current", "", nil), 404, nil)
	// Act
	var subscription domain.WishlistSubscriptionResponse
	created := s.member.send(t, "POST", path, "", nil)
	location := created.Header.Get("Location")
	requireStatus(t, created, 201, &subscription)
	// Assert
	if subscription.WishlistId != list.Id || subscription.Name != list.Name || subscription.ShareUrl != s.owner.service.Config.FrontendOrigin+strings.TrimPrefix(base, "/api/v1") || location != "/api/v1/wishlist-subscriptions/"+subscription.Id {
		t.Fatal("private follow response or location differs")
	}
	requireStatus(t, s.member.send(t, "GET", path+"/current", "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "POST", path, "", nil), 409, nil)
	requireStatus(t, s.foreign.send(t, "GET", location, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "DELETE", location, "", nil), 404, nil)
	var page domain.PaginatedResponse[domain.WishlistSubscriptionResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlist-subscriptions", "", nil), 200, &page)
	if page.TotalCount != 1 || len(page.Items) != 1 || page.Items[0].Id != subscription.Id || strings.Contains(page.Items[0].ShareUrl, "#") {
		t.Fatal("group follow gallery exposes an incorrect URL or count")
	}
	requireStatus(t, s.owner.send(t, "GET", owned+"/share-link", "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", base, "", nil), 200, &shared)
	if shared.CurrentParticipant != nil {
		t.Fatal("following enrolled the member")
	}
	reservation := base + "/wishes/" + wish.Id + "/reservations/current"
	requireStatus(t, s.member.send(t, "PUT", reservation, `{"quantity":1}`, nil), 201, nil)
	requireStatus(t, s.member.send(t, "DELETE", location, "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "GET", reservation, "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "GET", path+"/current", "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", base, "", nil), 200, nil)
}

func TestGroupSubscriptions_WhenGroupAccessWithdrawn_RevokesFollowWithoutRestoringIt(t *testing.T) {
	for _, action := range []string{"privateGrant", "publicLink", "publicExclusion", "subscriberLeaves", "ownerLeaves", "groupDeleted"} {
		t.Run(action, func(t *testing.T) {
			// Arrange
			s := newGroupScenario(t)
			s.accept(t, s.member, s.memberID)
			follower := s.owner
			if action == "subscriberLeaves" {
				s.accept(t, s.foreign, s.foreignID)
				follower = s.foreign
			}
			var list domain.WishlistResponse
			requireStatus(t, s.member.send(t, "POST", "/api/v1/wishlists", `{"name":"Private list","occasion":"other"}`, nil), 201, &list)
			owned := "/api/v1/wishlists/" + list.Id
			base := s.base + "/wishlists/" + list.Id
			public := action == "publicLink" || action == "publicExclusion"
			var link domain.WishlistShareLinkResponse
			var linkVersion string
			if public {
				created := s.member.send(t, "POST", owned+"/share-link", "", nil)
				linkVersion = created.Header.Get("ETag")
				requireStatus(t, created, 201, &link)
			} else {
				requireStatus(t, s.member.send(t, "PUT", base, "", nil), 201, nil)
			}
			var subscription domain.WishlistSubscriptionResponse
			requireStatus(t, follower.send(t, "POST", base+"/subscriptions", "", nil), 201, &subscription)
			// Act
			switch action {
			case "privateGrant", "publicExclusion":
				requireStatus(t, s.member.send(t, "DELETE", base, "", nil), 204, nil)
			case "publicLink":
				requireStatus(t, s.member.send(t, "DELETE", owned+"/share-link", "", map[string]string{"If-Match": linkVersion}), 204, nil)
			case "subscriberLeaves":
				requireStatus(t, follower.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
			case "ownerLeaves":
				requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.memberID, "", nil), 204, nil)
			case "groupDeleted":
				requireStatus(t, s.owner.send(t, "DELETE", s.base, "", map[string]string{"If-Match": s.etag}), 204, nil)
			}
			// Assert
			location := "/api/v1/wishlist-subscriptions/" + subscription.Id
			requireStatus(t, follower.send(t, "GET", location, "", nil), 404, nil)
			var page domain.PaginatedResponse[domain.WishlistSubscriptionResponse]
			requireStatus(t, follower.send(t, "GET", "/api/v1/wishlist-subscriptions", "", nil), 200, &page)
			if page.TotalCount != 0 || len(page.Items) != 0 {
				t.Fatal("withdrawn private access remains in followed lists")
			}
			if action == "privateGrant" || action == "publicExclusion" {
				requireStatus(t, s.member.send(t, "PUT", base, "", nil), 201, nil)
				requireStatus(t, s.owner.send(t, "GET", location, "", nil), 404, nil)
				requireStatus(t, s.owner.send(t, "GET", base+"/subscriptions/current", "", nil), 404, nil)
			}
		})
	}
}

func TestGroupSubscriptions_WhenUsingPublicAndPrivateEntries_SharesOneFollowWithoutChangingPublicAccess(t *testing.T) {
	for _, source := range []string{"group", "public"} {
		t.Run(source, func(t *testing.T) {
			// Arrange
			s := newGroupScenario(t)
			s.accept(t, s.member, s.memberID)
			var list domain.WishlistResponse
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Two entries","occasion":"other"}`, nil), 201, &list)
			var link domain.WishlistShareLinkResponse
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
			capability, err := url.Parse(link.ShareUrl)
			if err != nil {
				t.Fatal(err)
			}
			headers := map[string]string{"X-MonKado-Share-Token": capability.Fragment}
			group := s.base + "/wishlists/" + list.Id
			public := "/api/v1/shared-wishlists/" + link.Id
			path := group
			if source == "public" {
				path = public
			}
			// Act
			var created domain.WishlistSubscriptionResponse
			requireStatus(t, s.member.send(t, "POST", path+"/subscriptions", "", headers), 201, &created)
			// Assert
			for _, entry := range []string{group, public} {
				var current domain.WishlistSubscriptionResponse
				requireStatus(t, s.member.send(t, "GET", entry+"/subscriptions/current", "", headers), 200, &current)
				if current.Id != created.Id {
					t.Fatal("entry points disagree about follow state")
				}
				requireStatus(t, s.member.send(t, "POST", entry+"/subscriptions", "", headers), 409, nil)
			}
			requireStatus(t, s.owner.send(t, "DELETE", group, "", nil), 204, nil)
			requireStatus(t, s.member.send(t, "GET", public, "", headers), 200, nil)
			status := 404
			if source == "public" {
				status = 200
			}
			requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlist-subscriptions/"+created.Id, "", nil), status, nil)
		})
	}
}
