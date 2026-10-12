//go:build integration

package httpapi_test

import (
	"encoding/json"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestPublicMemberProfile_WhenListsAreShared_ExposesOnlyActivePublicMetadata(t *testing.T) {
	// Arrange
	f := newFixture(t)
	owner := f.member(t, "public-owner@example.test")
	now := time.Now().UTC().Truncate(time.Microsecond)
	f.service.Now = func() time.Time { return now }
	ownerAccess := f.access
	visitor := f.visitor(t)
	var empty domain.PublicMemberProfileResponse
	requireStatus(t, visitor.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil), 200, &empty)
	if empty.Wishlists == nil || len(empty.Wishlists) != 0 || empty.ProfileImageUrl != nil {
		t.Fatal("empty public profile contract differs")
	}
	var newest, older domain.WishlistResponse
	var newestLink domain.WishlistShareLinkResponse
	for _, item := range []struct{ name, mode string }{{"Older shared", "shared"}, {"Private hidden", "private"}, {"Archived hidden", "archived"}, {"Newest shared", "shared"}} {
		now = now.Add(time.Second)
		var list domain.WishlistResponse
		created := f.send(t, "POST", "/api/v1/wishlists", jsonInput(t, map[string]any{"name": item.name, "occasion": "other", "message": "private owner content"}), nil)
		version := created.Header.Get("ETag")
		requireStatus(t, created, 201, &list)
		path := "/api/v1/wishlists/" + list.Id
		if item.mode == "private" {
			continue
		}
		var link domain.WishlistShareLinkResponse
		requireStatus(t, f.send(t, "POST", path+"/share-link", "", nil), 201, &link)
		if item.mode == "archived" {
			requireStatus(t, f.send(t, "PATCH", path, `{"isArchived":true}`, map[string]string{"If-Match": version}), 200, nil)
		}
		if item.name == "Older shared" {
			older = list
		}
		if item.name == "Newest shared" {
			newest, newestLink = list, link
		}
	}
	// Act
	var result map[string]json.RawMessage
	requireStatus(t, visitor.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil), 200, &result)
	// Assert
	if len(result) != 5 || result["id"] == nil || result["displayName"] == nil || result["displayNameTag"] == nil || result["profileImageUrl"] == nil || result["wishlists"] == nil {
		t.Fatal("public profile disclosed an unexpected property")
	}
	var lists []map[string]json.RawMessage
	if err := json.Unmarshal(result["wishlists"], &lists); err != nil || len(lists) != 2 {
		t.Fatal("public profile included a private or archived list")
	}
	var profile domain.PublicMemberProfileResponse
	requireStatus(t, visitor.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil), 200, &profile)
	if profile.Wishlists[0].Id != newest.Id || profile.Wishlists[1].Id != older.Id || profile.Wishlists[0].ShareUrl != newestLink.ShareUrl {
		t.Fatal("public profile ordering or capability differs")
	}
	for _, list := range lists {
		if len(list) != 5 || list["id"] == nil || list["name"] == nil || list["occasion"] == nil || list["eventDate"] == nil || list["shareUrl"] == nil {
			t.Fatal("public summary disclosed wish or owner content")
		}
	}
	shareURL, err := url.Parse(profile.Wishlists[0].ShareUrl)
	if err != nil || shareURL.Fragment == "" || strings.Contains(shareURL.RawQuery, shareURL.Fragment) {
		t.Fatal("public share capability is not confined to the frontend fragment")
	}
	current := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	version := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Hidden from search","isVisibleInMemberSearch":false}`, map[string]string{"If-Match": version}), 200, nil)
	requireStatus(t, visitor.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil), 200, &profile)
	if profile.DisplayName != "Hidden from search" {
		t.Fatal("search preference hid the independently public profile")
	}
	requireStatus(t, visitor.send(t, "GET", "/api/v1/members/"+security.NewID()+"/profile", "", nil), 404, nil)
	f.access = ""
	f.csrfToken(t)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", `{"email":"unconfirmed-public@example.test","password":"A long test password 🎁","displayName":"Unconfirmed"}`, nil), 202, nil)
	var unconfirmed string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT id::text FROM users WHERE email='unconfirmed-public@example.test'`).Scan(&unconfirmed); err != nil {
		t.Fatal(err)
	}
	requireStatus(t, visitor.send(t, "GET", "/api/v1/members/"+unconfirmed+"/profile", "", nil), 404, nil)
	f.access = ownerAccess
}

func TestSharedWishlist_WhenOwnerUsesSurpriseMode_HidesAllReservationDerivedFields(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "surprise-sharing-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Owner surprise","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Reserved favorite","isFavorite":true}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	shareURL, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": shareURL.Fragment}
	visitor := f.visitor(t)
	path := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, visitor.send(t, "POST", path+"/participants", `{"displayName":"Guest"}`, headers), 201, nil)
	requireStatus(t, visitor.send(t, "PUT", path+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, headers), 201, nil)
	// Act / Assert
	var ownerView domain.SharedWishlistResponse
	requireStatus(t, f.send(t, "GET", path+"?availableOnly=true", "", headers), 200, &ownerView)
	if ownerView.CanSubscribe || len(ownerView.Wishes) != 1 || !ownerView.Wishes[0].IsFavorite || ownerView.Wishes[0].ReservedQuantity != nil || ownerView.Wishes[0].AvailableQuantity != nil || ownerView.Wishes[0].CurrentParticipantReservedQuantity != nil {
		t.Fatal("owner view disclosed surprise quantities or filtered the reserved gift")
	}
	var detail domain.SharedWishDetailResponse
	requireStatus(t, f.send(t, "GET", path+"/wishes/"+wish.Id, "", headers), 200, &detail)
	if !detail.IsFavorite || detail.ReservedQuantity != nil || detail.AvailableQuantity != nil {
		t.Fatal("owner detail disclosed reservation state")
	}
	var participantView domain.SharedWishlistResponse
	requireStatus(t, visitor.send(t, "GET", path+"?availableOnly=true", "", headers), 200, &participantView)
	if !participantView.CanSubscribe || len(participantView.Wishes) != 1 || participantView.Wishes[0].ReservedQuantity == nil || *participantView.Wishes[0].ReservedQuantity != 1 || participantView.Wishes[0].CurrentParticipantReservedQuantity == nil || *participantView.Wishes[0].CurrentParticipantReservedQuantity != 1 {
		t.Fatal("available filter hid the participant's own fully reserved gift")
	}
	anonymous := f.visitor(t)
	requireStatus(t, anonymous.send(t, "GET", path+"?availableOnly=true", "", headers), 200, &participantView)
	if len(participantView.Wishes) != 0 {
		t.Fatal("available filter exposed fully reserved gifts to another participant")
	}
}
