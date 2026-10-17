//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestSharedWishlist_WhenNamesAreRepeated_ExposesVerifiedAuthorAndOpensPublicProfile(t *testing.T) {
	// Arrange: both accounts have the same display name; identity must come from
	// the actual list owner, not a name search or caller-provided navigation hint.
	f := newFixture(t)
	owner := f.member(t, "author-profile-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Shared wishes","occasion":"other"}`, nil), 201, &list)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	parsed, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	viewer := f.member(t, "author-profile-viewer@example.test")
	for _, identity := range []string{f.access, ""} {
		f.access = identity
		// Act
		var shared domain.SharedWishlistResponse
		requireStatus(t, f.send(t, "GET", "/api/v1"+parsed.Path, "", map[string]string{"X-MonKado-Share-Token": parsed.Fragment}), 200, &shared)
		var profile domain.PublicMemberProfileResponse
		requireStatus(t, f.send(t, "GET", "/api/v1/members/"+shared.OwnerId+"/profile", "", nil), 200, &profile)
		// Assert: a shared read and a subsequent public-profile read identify the
		// same confirmed author for both a member and an anonymous visitor.
		if shared.OwnerId != owner || shared.OwnerId == viewer || profile.Id != owner || profile.DisplayName != shared.OwnerDisplayName {
			t.Fatal("shared list did not identify its actual author's public profile")
		}
	}
}
