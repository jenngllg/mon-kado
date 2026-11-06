//go:build integration

package httpapi_test

import (
	"net/url"
	"reflect"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestPrivateResources_WhenMemberSubstitutesForeignIDsAndVersions_RejectsWithoutEffects(t *testing.T) {
	// Arrange: the attacker knows real resource IDs and their current ETags.
	owner := newFixture(t)
	ownerID := owner.member(t, "intrusion-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Private target","occasion":"other"}`, nil), 201, &list)
	base := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	created := owner.send(t, "POST", base+"/wishes", `{"name":"Private wish","quantity":5}`, nil)
	wishTag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	wishPath := base + "/wishes/" + wish.Id
	var reservation domain.GiftReservationResponse
	reserved := owner.send(t, "PUT", wishPath+"/reservations/current", `{"quantity":2}`, nil)
	reservationTag := reserved.Header.Get("ETag")
	requireStatus(t, reserved, 201, &reservation)
	var share domain.WishlistShareLinkResponse
	shared := owner.send(t, "POST", base+"/share-link", "", nil)
	shareTag := shared.Header.Get("ETag")
	requireStatus(t, shared, 201, &share)
	read := owner.send(t, "GET", base, "", nil)
	listTag := read.Header.Get("ETag")
	requireStatus(t, read, 200, &list)
	collection := owner.send(t, "GET", base+"/wishes", "", nil)
	collectionTag := collection.Header.Get("ETag")
	var wishes domain.WishCollectionResponse
	requireStatus(t, collection, 200, &wishes)
	var visibility domain.WishlistGroupVisibilityResponse
	visible := owner.send(t, "GET", base+"/group-visibility", "", nil)
	visibilityTag := visible.Header.Get("ETag")
	requireStatus(t, visible, 200, &visibility)
	attacker := owner.visitor(t)
	attacker.member(t, "intrusion-attacker@example.test")
	var own domain.WishlistResponse
	requireStatus(t, attacker.send(t, "POST", "/api/v1/wishlists", `{"name":"Attacker list","occasion":"other"}`, nil), 201, &own)
	ownBase := "/api/v1/wishlists/" + own.Id
	var ownWish domain.WishResponse
	requireStatus(t, attacker.send(t, "POST", ownBase+"/wishes", `{"name":"Attacker wish"}`, nil), 201, &ownWish)
	copyInput := jsonInput(t, map[string]string{"destinationWishlistId": own.Id})

	// Act / Assert: valid inputs and stolen versions reach the authorization check.
	for _, test := range []struct {
		name, method, path, body, tag string
	}{
		{"readList", "GET", base, "", ""},
		{"updateList", "PUT", base, `{"name":"Stolen","occasion":"other"}`, listTag},
		{"archiveList", "PATCH", base, `{"isArchived":true}`, listTag},
		{"deleteList", "DELETE", base, "", listTag},
		{"readWishes", "GET", base + "/wishes", "", ""},
		{"createWish", "POST", base + "/wishes", `{"name":"Injected"}`, ""},
		{"reorderWishes", "PATCH", base + "/wishes", jsonInput(t, map[string]any{"wishIds": []string{wish.Id}}), collectionTag},
		{"readWish", "GET", wishPath, "", ""},
		{"updateWish", "PUT", wishPath, `{"name":"Stolen","quantity":5}`, wishTag},
		{"favoriteWish", "PATCH", wishPath, `{"isFavorite":true}`, wishTag},
		{"deleteWish", "DELETE", wishPath, "", wishTag},
		{"copyWish", "POST", wishPath + "/copies", copyInput, ""},
		{"readLink", "GET", base + "/share-link", "", ""},
		{"createLink", "POST", base + "/share-link", "", ""},
		{"rotateLink", "PUT", base + "/share-link", "", shareTag},
		{"deleteLink", "DELETE", base + "/share-link", "", shareTag},
		{"readAudience", "GET", base + "/group-visibility", "", ""},
		{"changeAudience", "PUT", base + "/group-visibility", `{"groups":[]}`, visibilityTag},
		{"readReservation", "GET", wishPath + "/reservations/current", "", ""},
		{"updateReservation", "PUT", wishPath + "/reservations/current", `{"quantity":1}`, reservationTag},
		{"deleteReservation", "DELETE", wishPath + "/reservations/current", "", reservationTag},
		{"mixedParentRead", "GET", ownBase + "/wishes/" + wish.Id, "", ""},
		{"mixedParentUpdate", "PUT", ownBase + "/wishes/" + wish.Id, `{"name":"Stolen","quantity":5}`, wishTag},
		{"mixedParentDelete", "DELETE", ownBase + "/wishes/" + wish.Id, "", wishTag},
		{"mixedParentCopy", "POST", ownBase + "/wishes/" + wish.Id + "/copies", jsonInput(t, map[string]string{"destinationWishlistId": list.Id}), ""},
		{"foreignCopyDestination", "POST", ownBase + "/wishes/" + ownWish.Id + "/copies", jsonInput(t, map[string]string{"destinationWishlistId": list.Id}), ""},
		{"mixedParentReservation", "PUT", ownBase + "/wishes/" + wish.Id + "/reservations/current", `{"quantity":1}`, reservationTag},
	} {
		t.Run(test.name, func(t *testing.T) {
			var failure domain.ErrorResponse
			requireStatus(t, attacker.send(t, test.method, test.path, test.body, map[string]string{"If-Match": test.tag}), 404, &failure)
			if failure.ErrorCode == nil || strings.Contains(*failure.Message, list.Name) || strings.Contains(*failure.Message, wish.Name) {
				t.Fatal("private resource rejection disclosed content or omitted its stable code")
			}
		})
	}
	var lists []domain.WishlistResponse
	requireStatus(t, attacker.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &lists)
	if len(lists) != 1 || lists[0].Id != own.Id {
		t.Fatal("private list collection leaked another member's list")
	}
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, attacker.send(t, "GET", "/api/v1/members/current/reservations?memberId="+ownerID, "", nil), 200, &history)
	if history.TotalCount != 0 || len(history.Items) != 0 {
		t.Fatal("reservation history leaked another member's reservation")
	}
	for _, check := range []struct {
		path, tag string
		expected  any
		target    any
	}{
		{base, listTag, &list, &domain.WishlistResponse{}},
		{base + "/wishes", collectionTag, &wishes, &domain.WishCollectionResponse{}},
		{wishPath, wishTag, &wish, &domain.WishResponse{}},
		{wishPath + "/reservations/current", reservationTag, &reservation, &domain.GiftReservationResponse{}},
		{base + "/share-link", shareTag, &share, &domain.WishlistShareLinkResponse{}},
		{base + "/group-visibility", visibilityTag, &visibility, &domain.WishlistGroupVisibilityResponse{}},
	} {
		response := owner.send(t, "GET", check.path, "", nil)
		if response.Header.Get("ETag") != check.tag {
			t.Errorf("rejected attack changed the version of %s", check.path)
		}
		requireStatus(t, response, 200, check.target)
		if !reflect.DeepEqual(check.expected, check.target) {
			t.Errorf("rejected attack changed %s", check.path)
		}
	}
	var destination domain.WishCollectionResponse
	requireStatus(t, attacker.send(t, "GET", ownBase+"/wishes", "", nil), 200, &destination)
	if len(destination.Wishes) != 1 || destination.Wishes[0].Id != ownWish.Id {
		t.Fatal("rejected copy persisted private content")
	}
}

func TestSharedReservations_WhenMemberSubstitutesShareOrParticipantIDs_PreservesOtherReservations(t *testing.T) {
	// Arrange
	owner := newFixture(t)
	owner.member(t, "share-owner@example.test")
	var lists [2]domain.WishlistResponse
	var wishes [2]domain.WishResponse
	var links [2]domain.WishlistShareLinkResponse
	secrets := [2]string{}
	for i, name := range []string{"First", "Second"} {
		requireStatus(t, owner.send(t, "POST", "/api/v1/wishlists", jsonInput(t, map[string]string{"name": name, "occasion": "other"}), nil), 201, &lists[i])
		base := "/api/v1/wishlists/" + lists[i].Id
		requireStatus(t, owner.send(t, "POST", base+"/wishes", `{"name":"Gift","quantity":5}`, nil), 201, &wishes[i])
		requireStatus(t, owner.send(t, "POST", base+"/share-link", "", nil), 201, &links[i])
		parsed, err := url.Parse(links[i].ShareUrl)
		if err != nil {
			t.Fatal(err)
		}
		secrets[i] = parsed.Fragment
	}
	participant := owner.visitor(t)
	participantID := participant.member(t, "share-participant@example.test")
	attacker := owner.visitor(t)
	attacker.member(t, "share-attacker@example.test")
	base := "/api/v1/shared-wishlists/" + links[0].Id
	headers := map[string]string{"X-MonKado-Share-Token": secrets[0]}
	var identity domain.WishlistParticipantResponse
	requireStatus(t, participant.send(t, "POST", base+"/participants", "", headers), 201, &identity)
	path := base + "/wishes/" + wishes[0].Id + "/reservations/current"
	var saved domain.GiftReservationResponse
	created := participant.send(t, "PUT", path, `{"quantity":2}`, headers)
	tag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &saved)

	// Act / Assert: an identifier alone, or another list's secret, grants no access.
	for _, secret := range []string{"", secrets[1]} {
		for _, method := range []string{"GET", "PUT", "DELETE"} {
			requireStatus(t, attacker.send(t, method, path, `{"quantity":1}`, map[string]string{"X-MonKado-Share-Token": secret, "If-Match": tag}), 404, nil)
		}
		requireStatus(t, attacker.send(t, "GET", base, "", map[string]string{"X-MonKado-Share-Token": secret}), 404, nil)
	}
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		requireStatus(t, attacker.send(t, method, base+"/wishes/"+wishes[1].Id+"/reservations/current", `{"quantity":1}`, map[string]string{"X-MonKado-Share-Token": secrets[0], "If-Match": tag}), 404, nil)
	}
	// Even a legitimate reader/participant cannot choose another reservation's identity.
	requireStatus(t, attacker.send(t, "GET", base, "", headers), 200, nil)
	requireStatus(t, attacker.send(t, "POST", base+"/participants", "", headers), 201, nil)
	stolen := map[string]string{"X-MonKado-Share-Token": secrets[0], "If-Match": tag}
	spoofed := path + "?participantId=" + identity.Id + "&memberId=" + participantID + "&reservationId=" + saved.Id
	requireStatus(t, attacker.send(t, "GET", spoofed, "", stolen), 404, nil)
	requireStatus(t, attacker.send(t, "DELETE", spoofed, "", stolen), 404, nil)
	requireStatus(t, attacker.send(t, "PUT", spoofed, `{"quantity":1}`, stolen), 412, nil)
	requireStatus(t, attacker.send(t, "GET", strings.TrimSuffix(path, "current")+saved.Id, "", headers), 404, nil)
	var own domain.GiftReservationResponse
	requireStatus(t, attacker.send(t, "PUT", spoofed, `{"quantity":1}`, headers), 201, &own)
	if own.Id == saved.Id || own.Quantity != 1 {
		t.Fatal("creating a personal reservation took over another participant's reservation")
	}
	var current domain.GiftReservationResponse
	read := participant.send(t, "GET", path, "", headers)
	if read.Header.Get("ETag") != tag {
		t.Fatal("another member changed the reservation version")
	}
	requireStatus(t, read, 200, &current)
	if !reflect.DeepEqual(saved, current) {
		t.Fatal("another member changed the reservation")
	}
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, attacker.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].Id != own.Id {
		t.Fatal("reservation history disclosed another participant's reservation")
	}
}

func TestGroupResources_WhenMemberSubstitutesGroupListWishOrReservationIDs_RequiresCurrentGrant(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var list, private domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Shared in group","occasion":"other"}`, nil), 201, &list)
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Never shared","occasion":"other"}`, nil), 201, &private)
	var wish, privateWish domain.WishResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Shared gift","quantity":5}`, nil), 201, &wish)
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+private.Id+"/wishes", `{"name":"Private gift"}`, nil), 201, &privateWish)
	base := s.base + "/wishlists/" + list.Id
	requireStatus(t, s.owner.send(t, "PUT", base, "", nil), 201, nil)
	path := base + "/wishes/" + wish.Id + "/reservations/current"
	var saved domain.GiftReservationResponse
	created := s.member.send(t, "PUT", path, `{"quantity":2}`, nil)
	tag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &saved)
	var otherGroup domain.GroupResponse
	requireStatus(t, s.member.send(t, "POST", "/api/v1/groups", `{"name":"Other group"}`, nil), 201, &otherGroup)
	wrongGroup := "/api/v1/groups/" + otherGroup.Id + "/wishlists/" + list.Id

	// Act / Assert: knowing real IDs never substitutes for membership AND list sharing.
	for _, caller := range []*fixture{s.foreign, s.member} {
		paths := []string{s.base + "/wishlists/" + private.Id, wrongGroup}
		if caller == s.foreign {
			paths = append(paths, base)
		}
		for _, target := range paths {
			requireStatus(t, caller.send(t, "GET", target, "", nil), 404, nil)
			for _, method := range []string{"GET", "PUT", "DELETE"} {
				requireStatus(t, caller.send(t, method, target+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, map[string]string{"If-Match": tag}), 404, nil)
			}
		}
	}
	for _, method := range []string{"PUT", "DELETE"} {
		requireStatus(t, s.member.send(t, method, base, "", nil), 404, nil)
	}
	requireStatus(t, s.member.send(t, "GET", base+"/wishes/"+privateWish.Id, "", nil), 404, nil)
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		requireStatus(t, s.member.send(t, method, base+"/wishes/"+privateWish.Id+"/reservations/current", `{"quantity":1}`, map[string]string{"If-Match": tag}), 404, nil)
	}
	// A newly authorized peer may create only their own reservation.
	s.accept(t, s.foreign, s.foreignID)
	spoofed := path + "?memberId=" + s.memberID + "&reservationId=" + saved.Id
	requireStatus(t, s.foreign.send(t, "GET", base, "", nil), 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", spoofed, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "DELETE", spoofed, "", map[string]string{"If-Match": tag}), 404, nil)
	requireStatus(t, s.foreign.send(t, "PUT", spoofed, `{"quantity":1}`, map[string]string{"If-Match": tag}), 412, nil)
	var own domain.GiftReservationResponse
	requireStatus(t, s.foreign.send(t, "PUT", spoofed, `{"quantity":1}`, nil), 201, &own)
	if own.Id == saved.Id {
		t.Fatal("another group member took over the reservation")
	}
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.foreignID, "", nil), 204, nil)
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		requireStatus(t, s.foreign.send(t, method, path, `{"quantity":1}`, map[string]string{"If-Match": tag}), 404, nil)
	}
	var current domain.GiftReservationResponse
	read := s.member.send(t, "GET", path, "", nil)
	if read.Header.Get("ETag") != tag {
		t.Fatal("rejected group attacks changed another reservation's version")
	}
	requireStatus(t, read, 200, &current)
	if !reflect.DeepEqual(saved, current) {
		t.Fatal("rejected group attacks changed another reservation")
	}
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].Id != saved.Id {
		t.Fatal("group reservation history disclosed another member's reservation")
	}
}
