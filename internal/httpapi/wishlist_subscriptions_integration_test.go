//go:build integration

package httpapi_test

import (
	"net/url"
	"reflect"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

type subscriptionScenario struct {
	owner, member *fixture
	list          domain.WishlistResponse
	link          domain.WishlistShareLinkResponse
	headers       map[string]string
	shared, base  string
}

func newSubscriptionScenario(t *testing.T) subscriptionScenario {
	t.Helper()
	f := newFixture(t)
	f.member(t, "subscription-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Followed birthday","occasion":"birthday","eventDate":"2027-01-18"}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", base+"/share-link", "", nil), 201, &link)
	u, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	member := f.visitor(t)
	member.member(t, "subscriber@example.test")
	return subscriptionScenario{f, member, list, link, map[string]string{"X-MonKado-Share-Token": u.Fragment}, "/api/v1/shared-wishlists/" + link.Id, base}
}

func TestWishlistSubscriptions_WhenFollowingAndRemoving_PreservesMemberScopedLifecycle(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	path := s.shared + "/subscriptions"
	collection := "/api/v1/wishlist-subscriptions"
	var page domain.PaginatedResponse[domain.WishlistSubscriptionResponse]
	// Act / Assert: the empty collection is a real 200 response, and reads never subscribe.
	requireStatus(t, s.member.send(t, "GET", collection, "", nil), 200, &page)
	if page.Items == nil || len(page.Items) != 0 || page.TotalPages != 0 || page.HasPreviousPage || page.HasNextPage {
		t.Fatal("invalid empty collection metadata")
	}
	var absent domain.ErrorResponse
	requireStatus(t, s.member.send(t, "GET", path+"/current", "", s.headers), 404, &absent)
	if absent.ErrorCode == nil || *absent.ErrorCode != domain.WishlistSubscriptionNotFound {
		t.Fatal("absent follow state has the wrong code")
	}
	requireStatus(t, s.owner.send(t, "POST", path, "", s.headers), 409, &absent)
	if absent.ErrorCode == nil || *absent.ErrorCode != domain.WishlistSubscriptionSelf {
		t.Fatal("self follow was not rejected")
	}
	var saved domain.WishlistSubscriptionResponse
	created := s.member.send(t, "POST", path, "", s.headers)
	location := created.Header.Get("Location")
	requireStatus(t, created, 201, &saved)
	if location != collection+"/"+saved.Id || saved.WishlistId != s.list.Id || saved.Name != s.list.Name || saved.Occasion != "birthday" || saved.EventDate == nil || *saved.EventDate != "2027-01-18" || saved.ShareUrl != s.link.ShareUrl || saved.CreatedAt.IsZero() {
		t.Fatal("created follow contract differs")
	}
	var current domain.WishlistSubscriptionResponse
	requireStatus(t, s.member.send(t, "GET", location, "", nil), 200, &current)
	if !reflect.DeepEqual(saved, current) {
		t.Fatal("saved follow was not persisted")
	}
	requireStatus(t, s.member.send(t, "GET", path+"/current", "", s.headers), 200, &current)
	requireStatus(t, s.member.send(t, "POST", path, "", s.headers), 409, &absent)
	if absent.ErrorCode == nil || *absent.ErrorCode != domain.WishlistSubscriptionAlreadyExists {
		t.Fatal("duplicate follow was not rejected")
	}
	requireStatus(t, s.member.send(t, "GET", collection+"?pageSize=1", "", nil), 200, &page)
	if page.TotalCount != 1 || len(page.Items) != 1 || page.Items[0].Id != saved.Id {
		t.Fatal("follow missing from own collection")
	}
	requireStatus(t, s.member.send(t, "GET", collection+"?page=2147483647", "", nil), 200, &page)
	if page.CurrentPage != 2147483647 || page.TotalCount != 1 || len(page.Items) != 0 || !page.HasPreviousPage || page.HasNextPage {
		t.Fatal("out-of-range metadata lost")
	}
	requireStatus(t, s.owner.send(t, "GET", collection, "", nil), 200, &page)
	if page.TotalCount != 0 {
		t.Fatal("follow disclosed to another member")
	}
	for _, method := range []string{"GET", "DELETE"} {
		requireStatus(t, s.owner.send(t, method, location, "", nil), 404, nil)
	}
	// Following is independent of participation and reservation.
	requireStatus(t, s.member.send(t, "GET", s.shared+"/participants/current", "", s.headers), 404, nil)
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if history.TotalCount != 0 {
		t.Fatal("following reserved an item")
	}
	requireStatus(t, s.member.send(t, "DELETE", location, "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "DELETE", location, "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", collection, "", nil), 200, &page)
	if page.TotalCount != 0 {
		t.Fatal("unfollow was not persisted")
	}
}

func TestWishlistSubscriptions_WhenCapabilityOrCallerIsInvalid_RejectsAccessAndMutation(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	path := s.shared + "/subscriptions"
	visitor := s.owner.visitor(t)
	// Act / Assert
	for _, endpoint := range []struct{ method, path string }{{"GET", "/api/v1/wishlist-subscriptions"}, {"GET", path + "/current"}, {"POST", path}, {"DELETE", "/api/v1/wishlist-subscriptions/" + security.NewID()}} {
		requireStatus(t, visitor.send(t, endpoint.method, endpoint.path, "", s.headers), 401, nil)
	}
	for _, method := range []string{"GET", "POST"} {
		target := path
		if method == "GET" {
			target += "/current"
		}
		for _, headers := range []map[string]string{nil, {"X-MonKado-Share-Token": "invalid"}} {
			requireStatus(t, s.member.send(t, method, target, "", headers), 404, nil)
		}
	}
	for _, query := range []string{"?page=0", "?pageSize=0", "?pageSize=101", "?page=", "?page=2147483648"} {
		requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlist-subscriptions"+query, "", nil), 400, nil)
	}
	for _, id := range []string{"invalid", "00000000-0000-0000-0000-000000000000", security.NewID()} {
		requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlist-subscriptions/"+id, "", nil), 404, nil)
	}
	csrf := s.member.csrf
	s.member.csrf = ""
	requireStatus(t, s.member.send(t, "POST", path, "", s.headers), 400, nil)
	s.member.csrf = csrf
	var saved domain.WishlistSubscriptionResponse
	requireStatus(t, s.member.send(t, "POST", path, "", s.headers), 201, &saved)
	s.member.csrf = ""
	requireStatus(t, s.member.send(t, "DELETE", "/api/v1/wishlist-subscriptions/"+saved.Id, "", nil), 400, nil)
	s.member.csrf = csrf
	requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlist-subscriptions/"+saved.Id, "", nil), 200, nil)
}

func TestWishlistSubscriptions_WhenCapabilityIsWithdrawn_RevokesWhileArchiveRestorationPreservesFollow(t *testing.T) {
	for _, action := range []string{"rotate", "revoke", "archive", "suspend", "delete"} {
		t.Run(action, func(t *testing.T) {
			// Arrange
			s := newSubscriptionScenario(t)
			var saved domain.WishlistSubscriptionResponse
			requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, &saved)
			read := s.owner.send(t, "GET", s.base, "", nil)
			version := read.Header.Get("ETag")
			requireStatus(t, read, 200, nil)
			// Act
			switch action {
			case "rotate", "revoke":
				read = s.owner.send(t, "GET", s.base+"/share-link", "", nil)
				tag := read.Header.Get("ETag")
				requireStatus(t, read, 200, nil)
				method, status := "PUT", 200
				if action == "revoke" {
					method, status = "DELETE", 204
				}
				requireStatus(t, s.owner.send(t, method, s.base+"/share-link", "", map[string]string{"If-Match": tag}), status, nil)
			case "archive":
				r := s.owner.send(t, "PATCH", s.base, `{"isArchived":true}`, map[string]string{"If-Match": version})
				next := r.Header.Get("ETag")
				requireStatus(t, r, 200, nil)
				requireStatus(t, s.owner.send(t, "PATCH", s.base, `{"isArchived":false}`, map[string]string{"If-Match": next}), 200, nil)
			case "suspend":
				// Technical invariant: the database trigger protects all moderation update paths.
				if _, err := s.owner.store.Pool.Exec(t.Context(), `UPDATE wishlists SET is_suspended=true,suspension_reason='Test moderation',suspended_at=now() WHERE id=$1`, s.list.Id); err != nil {
					t.Fatal(err)
				}
				if _, err := s.owner.store.Pool.Exec(t.Context(), `UPDATE wishlists SET is_suspended=false,suspension_reason=NULL,suspended_at=NULL WHERE id=$1`, s.list.Id); err != nil {
					t.Fatal(err)
				}
			case "delete":
				requireStatus(t, s.owner.send(t, "DELETE", s.base, "", map[string]string{"If-Match": version}), 204, nil)
			}
			// Assert
			var page domain.PaginatedResponse[domain.WishlistSubscriptionResponse]
			requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlist-subscriptions", "", nil), 200, &page)
			expected := 0
			if action == "archive" || action == "suspend" {
				expected = 1
			}
			if page.TotalCount != expected || len(page.Items) != expected {
				t.Fatal("subscription does not match the current capability")
			}
			status := 404
			if expected == 1 {
				status = 200
			}
			requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlist-subscriptions/"+saved.Id, "", nil), status, nil)
		})
	}
}
