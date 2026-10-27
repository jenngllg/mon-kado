//go:build integration

package httpapi_test

import (
	"fmt"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func groupVisibilityList(t *testing.T, s groupScenario) domain.WishlistResponse {
	t.Helper()
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Audience test","occasion":"other"}`, nil), 201, &list)
	return list
}

func publicGroupVisibilityList(t *testing.T, s groupScenario, list domain.WishlistResponse) domain.WishlistShareLinkResponse {
	t.Helper()
	var link domain.WishlistShareLinkResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	return link
}

func readGroupVisibility(t *testing.T, s groupScenario, list domain.WishlistResponse) (domain.WishlistGroupVisibilityResponse, string) {
	t.Helper()
	response := s.owner.send(t, "GET", "/api/v1/wishlists/"+list.Id+"/group-visibility", "", nil)
	tag := response.Header.Get("ETag")
	var result domain.WishlistGroupVisibilityResponse
	requireStatus(t, response, 200, &result)
	if result.WishlistId != list.Id || tag == "" {
		t.Fatal("missing owned visibility identity or version")
	}
	return result, tag
}

func TestWishlistGroupVisibility_WhenPublic_DefaultsToAllGroupsAndPreservesExclusions(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	list := groupVisibilityList(t, s)
	link := publicGroupVisibilityList(t, s, list)
	path := "/api/v1/wishlists/" + list.Id + "/group-visibility"
	result, tag := readGroupVisibility(t, s, list)
	if !result.IsPublic || len(result.Groups) != 1 || !result.Groups[0].IsVisible {
		t.Fatal("public list was not visible by default")
	}
	requireStatus(t, s.member.send(t, "GET", s.base+"/wishlists/"+list.Id, "", nil), 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", s.base+"/wishlists/"+list.Id, "", nil), 404, nil)
	// Act
	body := fmt.Sprintf(`{"groups":[{"groupId":%q,"isVisible":false}]}`, s.group.Id)
	response := s.owner.send(t, "PUT", path, body, map[string]string{"If-Match": tag})
	newTag := response.Header.Get("ETag")
	requireStatus(t, response, 200, &result)
	// Assert: the checkbox affects group access, never the independent public link.
	if result.Groups[0].IsVisible || newTag == tag {
		t.Fatal("exclusion or concurrency version not saved")
	}
	requireStatus(t, s.member.send(t, "GET", s.base+"/wishlists/"+list.Id, "", nil), 404, nil)
	requireStatus(t, s.owner.visitor(t).send(t, "GET", "/api/v1/shared-wishlists/"+link.Id, "", map[string]string{"X-MonKado-Share-Token": strings.Split(link.ShareUrl, "#")[1]}), 200, nil)
	readBack, readTag := readGroupVisibility(t, s, list)
	if readBack.Groups[0].IsVisible || readTag != newTag {
		t.Fatal("read did not preserve the exclusion without writing")
	}
	shareRead := s.owner.send(t, "GET", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil)
	shareTag := shareRead.Header.Get("ETag")
	requireStatus(t, shareRead, 200, nil)
	requireStatus(t, s.owner.send(t, "PUT", "/api/v1/wishlists/"+list.Id+"/share-link", "", map[string]string{"If-Match": shareTag}), 200, nil)
	readBack, _ = readGroupVisibility(t, s, list)
	if readBack.Groups[0].IsVisible {
		t.Fatal("rotating the public link restored an excluded group")
	}
	requireStatus(t, s.owner.send(t, "PUT", path, body, map[string]string{"If-Match": tag}), 412, nil)
}

func TestWishlistGroupVisibility_WhenJoiningLater_DefaultsNewGroupsWithoutChangingOldExclusions(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	list := groupVisibilityList(t, s)
	publicGroupVisibilityList(t, s, list)
	_, tag := readGroupVisibility(t, s, list)
	path := "/api/v1/wishlists/" + list.Id + "/group-visibility"
	requireStatus(t, s.owner.send(t, "PUT", path, fmt.Sprintf(`{"groups":[{"groupId":%q,"isVisible":false}]}`, s.group.Id), map[string]string{"If-Match": tag}), 200, nil)
	// Act: creating a group is also a new membership after the list became public.
	var later domain.GroupResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/groups", `{"name":"Later group"}`, nil), 201, &later)
	joined := s
	joined.group = later
	joined.base = "/api/v1/groups/" + later.Id
	joined.accept(t, s.member, s.memberID)
	// Assert
	result, _ := readGroupVisibility(t, s, list)
	if len(result.Groups) != 2 {
		t.Fatal("new membership missing from the checkbox audience")
	}
	for _, group := range result.Groups {
		if group.IsVisible != (group.GroupId == later.Id) {
			t.Fatal("new default or old exclusion changed")
		}
	}
	requireStatus(t, s.member.send(t, "GET", joined.base+"/wishlists/"+list.Id, "", nil), 200, nil)
}

func TestWishlistGroupVisibility_WhenPrivate_RequiresExplicitGrantAndPreservesItAfterPublicRevocation(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	list := groupVisibilityList(t, s)
	path := "/api/v1/wishlists/" + list.Id + "/group-visibility"
	result, tag := readGroupVisibility(t, s, list)
	if result.IsPublic || result.Groups[0].IsVisible {
		t.Fatal("private list was automatically shared")
	}
	requireStatus(t, s.member.send(t, "GET", s.base+"/wishlists/"+list.Id, "", nil), 404, nil)
	// Act
	requireStatus(t, s.owner.send(t, "PUT", path, fmt.Sprintf(`{"groups":[{"groupId":%q,"isVisible":true}]}`, s.group.Id), map[string]string{"If-Match": tag}), 200, &result)
	// Assert
	if result.IsPublic || !result.Groups[0].IsVisible {
		t.Fatal("private grant changed public visibility")
	}
	requireStatus(t, s.owner.send(t, "GET", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", s.base+"/wishlists/"+list.Id, "", nil), 200, nil)
	publicGroupVisibilityList(t, s, list)
	linkRead := s.owner.send(t, "GET", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil)
	linkTag := linkRead.Header.Get("ETag")
	requireStatus(t, linkRead, 200, nil)
	requireStatus(t, s.owner.send(t, "DELETE", "/api/v1/wishlists/"+list.Id+"/share-link", "", map[string]string{"If-Match": linkTag}), 204, nil)
	result, _ = readGroupVisibility(t, s, list)
	if result.IsPublic || !result.Groups[0].IsVisible {
		t.Fatal("public revocation removed the independent private grant")
	}
	requireStatus(t, s.member.send(t, "GET", s.base+"/wishlists/"+list.Id, "", nil), 200, nil)
}

func TestWishlistGroupVisibility_WhenRequestInvalid_ProtectsOwnershipVersionsAndAtomicity(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	list := groupVisibilityList(t, s)
	publicGroupVisibilityList(t, s, list)
	path := "/api/v1/wishlists/" + list.Id + "/group-visibility"
	_, tag := readGroupVisibility(t, s, list)
	body := fmt.Sprintf(`{"groups":[{"groupId":%q,"isVisible":false}]}`, s.group.Id)
	// Act / Assert
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "PUT", path, body, map[string]string{"If-Match": tag}), 404, nil)
	requireStatus(t, s.owner.visitor(t).send(t, "GET", path, "", nil), 401, nil)
	requireStatus(t, s.owner.send(t, "PUT", path, body, nil), 428, nil)
	requireStatus(t, s.owner.send(t, "PUT", path, body, map[string]string{"If-Match": "bad"}), 400, nil)
	var foreign domain.GroupResponse
	requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups", `{"name":"Foreign group"}`, nil), 201, &foreign)
	bad := fmt.Sprintf(`{"groups":[{"groupId":%q,"isVisible":false},{"groupId":%q,"isVisible":true}]}`, s.group.Id, foreign.Id)
	requireStatus(t, s.owner.send(t, "PUT", path, bad, map[string]string{"If-Match": tag}), 404, nil)
	result, currentTag := readGroupVisibility(t, s, list)
	if !result.Groups[0].IsVisible || currentTag != tag {
		t.Fatal("a failed multi-group write committed partial choices")
	}
	var failure domain.ErrorResponse
	requireStatus(t, s.owner.send(t, "PUT", path, `{"groups":[{"groupId":"bad"}]}`, map[string]string{"If-Match": tag}), 400, &failure)
	if len(failure.ValidationErrors) != 2 || failure.ValidationErrors[0].PropertyName != "groups[0].groupId" || failure.ValidationErrors[1].PropertyName != "groups[0].isVisible" {
		t.Fatal("nested validation paths were not aggregated")
	}
}

func TestWishlistGroupVisibility_WhenPublicGroupExcluded_RevokesImagesAndHistoryAccess(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	list := groupVisibilityList(t, s)
	publicGroupVisibilityList(t, s, list)
	own := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	created := s.owner.send(t, "POST", own+"/wishes", `{"name":"Group photo"}`, nil)
	wishTag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	requireStatus(t, s.owner.upload(t, own+"/wishes/"+wish.Id+"/image", wishTag, 42), 200, &wish)
	groupWish := s.base + "/wishlists/" + list.Id + "/wishes/" + wish.Id
	var detail domain.SharedWishDetailResponse
	requireStatus(t, s.member.send(t, "GET", groupWish, "", nil), 200, &detail)
	if detail.ImageUrl == nil {
		t.Fatal("default group audience did not authorize a photo")
	}
	grant, err := url.Parse(*detail.ImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, s.member.send(t, "GET", grant.RequestURI(), "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "PUT", groupWish+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
	// Act
	_, tag := readGroupVisibility(t, s, list)
	requireStatus(t, s.owner.send(t, "PUT", own+"/group-visibility", fmt.Sprintf(`{"groups":[{"groupId":%q,"isVisible":false}]}`, s.group.Id), map[string]string{"If-Match": tag}), 200, nil)
	// Assert
	requireStatus(t, s.member.send(t, "GET", grant.RequestURI(), "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", groupWish+"/reservations/current", "", nil), 404, nil)
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].GroupWishPath != nil || history.Items[0].ImageUrl != nil || history.Items[0].ShareUrl != nil || history.Items[0].InaccessibilityReason == nil {
		t.Fatal("group exclusion disclosed a private image or reconstructed an independent public capability")
	}
}
