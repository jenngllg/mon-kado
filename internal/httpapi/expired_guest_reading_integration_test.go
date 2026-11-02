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

func TestPublicReading_WhenGuestIdentityIsInvalid_RemainsReadableWithoutReservationAccess(t *testing.T) {
	for _, scenario := range []string{"malformed", "missing", "expired", "wrongSecret"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
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
			headers := map[string]string{"X-MonKado-Share-Token": sharedURL.Fragment}
			path := "/api/v1/shared-wishlists/" + link.Id
			guest := f.visitor(t)
			originalClock := f.service.Now
			var original domain.WishlistParticipantResponse
			requireStatus(t, guest.send(t, "POST", path+"/participants", `{"displayName":"Original guest"}`, headers), 201, &original)
			reservation := path + "/wishes/" + wish.Id + "/reservations/current"
			requireStatus(t, guest.send(t, "PUT", reservation, `{"quantity":1}`, headers), 201, nil)
			baseURL, _ := url.Parse(f.server.URL)
			var cookie *http.Cookie
			for _, candidate := range guest.client.Jar.Cookies(baseURL) {
				if candidate.Name == "MonKado.Guest" {
					cookie = candidate
				}
			}
			if cookie == nil {
				t.Fatal("guest cookie was not issued")
			}
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
				now := f.service.Now()
				f.service.Now = func() time.Time { return now.Add(181 * 24 * time.Hour) }
			}
			guest.client.Jar.SetCookies(baseURL, []*http.Cookie{cookie})

			// Act
			var shared domain.SharedWishlistResponse
			response := guest.send(t, "GET", path, "", headers)
			requireStatus(t, response, 200, &shared)
			var detail domain.SharedWishDetailResponse
			requireStatus(t, guest.send(t, "GET", path+"/wishes/"+wish.Id, "", headers), 200, &detail)

			// Assert
			if shared.CurrentParticipant != nil || detail.CurrentParticipantReservedQuantity != nil {
				t.Fatal("an invalid identity disclosed a personal reservation")
			}
			if !shared.CanSubscribe {
				t.Fatal("public readers were not offered the existing follow flow")
			}
			if len(shared.Wishes) != 1 || detail.ReservedQuantity == nil || *detail.ReservedQuantity != 1 {
				t.Fatal("the existing reservation was not preserved in public aggregates")
			}
			for _, returned := range response.Cookies() {
				if returned.Name == "MonKado.Guest" && returned.Value != "" {
					t.Fatal("a read issued a guest identity")
				}
			}
			requireStatus(t, guest.send(t, "GET", reservation, "", headers), 401, nil)
			requireStatus(t, guest.send(t, "PUT", reservation, `{"quantity":2}`, headers), 401, nil)
			var renewed domain.WishlistParticipantResponse
			requireStatus(t, guest.send(t, "POST", path+"/participants", `{"displayName":"New guest"}`, headers), 201, &renewed)
			if renewed.Id == original.Id {
				t.Fatal("a new identity was assigned the previous participant")
			}
			requireStatus(t, guest.send(t, "GET", reservation, "", headers), 404, nil)
			requireStatus(t, guest.send(t, "GET", path, "", headers), 200, &shared)
			if shared.CurrentParticipant == nil || shared.CurrentParticipant.Id != renewed.Id || *shared.Wishes[0].ReservedQuantity != 1 {
				t.Fatal("renewed participation changed existing reservations")
			}
			f.service.Now = originalClock
			requireStatus(t, f.send(t, "DELETE", "/api/v1/wishlists/"+list.Id+"/share-link", "", map[string]string{"If-Match": etag}), 204, nil)
			requireStatus(t, guest.send(t, "GET", path, "", headers), 404, nil)
		})
	}
}
