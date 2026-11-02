//go:build integration

package httpapi_test

import (
	"net/http"
	"net/url"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

type publicGuestScenario struct {
	owner, guest                                  *fixture
	path, wishPath, reservation, wishlistID, etag string
	headers                                       map[string]string
	original                                      domain.WishlistParticipantResponse
	clock                                         func() time.Time
}

func TestPublicReading_WhenGuestIdentityIsInvalid_RemainsReadableWithoutReservationAccess(t *testing.T) {
	for _, scenario := range []string{"malformed", "missing", "expired", "wrongSecret"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			s := newPublicGuestScenario(t)
			invalidatePublicGuest(t, s, scenario)
			// Act / Assert: each helper uses only public HTTP contracts.
			assertAnonymousPublicReading(t, s)
			assertGuestReservationIsolation(t, s)
			assertPublicLinkRevocation(t, s)
		})
	}
}

func newPublicGuestScenario(t *testing.T) publicGuestScenario {
	t.Helper()
	f := newFixture(t)
	f.member(t, "public-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Public list","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Wish","quantity":3}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	created := f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &link)
	sharedURL, _ := url.Parse(link.ShareUrl)
	s := publicGuestScenario{owner: f, guest: f.visitor(t), path: "/api/v1/shared-wishlists/" + link.Id, wishlistID: list.Id, etag: etag,
		headers: map[string]string{"X-MonKado-Share-Token": sharedURL.Fragment}, clock: f.service.Now}
	requireStatus(t, s.guest.send(t, "POST", s.path+"/participants", `{"displayName":"Original guest"}`, s.headers), 201, &s.original)
	s.wishPath = s.path + "/wishes/" + wish.Id
	s.reservation = s.wishPath + "/reservations/current"
	requireStatus(t, s.guest.send(t, "PUT", s.reservation, `{"quantity":1}`, s.headers), 201, nil)
	return s
}

func publicGuestCookie(t *testing.T, s publicGuestScenario) *http.Cookie {
	t.Helper()
	baseURL, _ := url.Parse(s.owner.server.URL)
	for _, cookie := range s.guest.client.Jar.Cookies(baseURL) {
		if cookie.Name == "MonKado.Guest" {
			return cookie
		}
	}
	t.Fatal("guest cookie was not issued")
	return nil
}

func invalidatePublicGuest(t *testing.T, s publicGuestScenario, scenario string) {
	t.Helper()
	cookie := publicGuestCookie(t, s)
	switch scenario {
	case "malformed":
		cookie.Value = "invalid"
	case "missing":
		cookie.Value = security.SessionToken(security.NewID())
	case "wrongSecret":
		id, err := security.SessionID(cookie.Value)
		if err != nil {
			t.Fatal(err)
		}
		cookie.Value = security.SessionToken(id)
	case "expired":
		now := s.owner.service.Now()
		s.owner.service.Now = func() time.Time { return now.Add(181 * 24 * time.Hour) }
	}
	baseURL, _ := url.Parse(s.owner.server.URL)
	s.guest.client.Jar.SetCookies(baseURL, []*http.Cookie{cookie})
}

func assertAnonymousPublicReading(t *testing.T, s publicGuestScenario) {
	t.Helper()
	var shared domain.SharedWishlistResponse
	response := s.guest.send(t, "GET", s.path, "", s.headers)
	requireStatus(t, response, 200, &shared)
	var detail domain.SharedWishDetailResponse
	requireStatus(t, s.guest.send(t, "GET", s.wishPath, "", s.headers), 200, &detail)
	if shared.CurrentParticipant != nil || detail.CurrentParticipantReservedQuantity != nil {
		t.Fatal("invalid identity disclosed a personal reservation")
	}
	if !shared.CanSubscribe {
		t.Fatal("public readers were not offered the existing follow flow")
	}
	if len(shared.Wishes) != 1 || detail.ReservedQuantity == nil || *detail.ReservedQuantity != 1 {
		t.Fatal("existing reservation was not preserved in public aggregates")
	}
	for _, returned := range response.Cookies() {
		if returned.Name == "MonKado.Guest" && returned.Value != "" {
			t.Fatal("a read issued a guest identity")
		}
	}
}

func assertGuestReservationIsolation(t *testing.T, s publicGuestScenario) {
	t.Helper()
	requireStatus(t, s.guest.send(t, "GET", s.reservation, "", s.headers), 401, nil)
	requireStatus(t, s.guest.send(t, "PUT", s.reservation, `{"quantity":2}`, s.headers), 401, nil)
	var renewed domain.WishlistParticipantResponse
	requireStatus(t, s.guest.send(t, "POST", s.path+"/participants", `{"displayName":"New guest"}`, s.headers), 201, &renewed)
	if renewed.Id == s.original.Id {
		t.Fatal("new identity was assigned the previous participant")
	}
	requireStatus(t, s.guest.send(t, "GET", s.reservation, "", s.headers), 404, nil)
	var shared domain.SharedWishlistResponse
	requireStatus(t, s.guest.send(t, "GET", s.path, "", s.headers), 200, &shared)
	if shared.CurrentParticipant == nil || shared.CurrentParticipant.Id != renewed.Id || *shared.Wishes[0].ReservedQuantity != 1 {
		t.Fatal("renewed participation changed existing reservations")
	}
}

func assertPublicLinkRevocation(t *testing.T, s publicGuestScenario) {
	t.Helper()
	s.owner.service.Now = s.clock
	requireStatus(t, s.owner.send(t, "DELETE", "/api/v1/wishlists/"+s.wishlistID+"/share-link", "", map[string]string{"If-Match": s.etag}), 204, nil)
	requireStatus(t, s.guest.send(t, "GET", s.path, "", s.headers), 404, nil)
}
