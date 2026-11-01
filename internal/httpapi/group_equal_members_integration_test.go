//go:build integration

package httpapi_test

import (
	"encoding/json"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestGroupMembers_WhenManagingAndRemovingCreator_KeepEqualAccess(t *testing.T) {
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	read := s.member.send(t, "GET", s.base, "", nil)
	etag := read.Header.Get("ETag")
	requireStatus(t, read, 200, nil)
	updated := s.member.send(t, "PUT", s.base, `{"name":"Renamed by member"}`, map[string]string{"If-Match": etag})
	requireStatus(t, updated, 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/"+s.ownerID, "", nil), 204, nil)
	requireStatus(t, s.owner.send(t, "GET", s.base, "", nil), 404, nil)
	var remaining domain.GroupResponse
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 200, &remaining)
	if remaining.MemberCount != 1 || remaining.OwnerId != s.memberID || remaining.Name != "Renamed by member" {
		t.Fatal("remaining group was not preserved")
	}
	requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 404, nil)
	var count int
	if err := s.owner.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM private_groups WHERE id=$1`, s.group.Id).Scan(&count); err != nil || count != 0 {
		t.Fatal("empty group was not removed")
	}
}

func TestGroupMembers_WhenCreatorDeletesAccount_RemainingMemberKeepsGroupAndPhoto(t *testing.T) {
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var uploaded domain.GroupResponse
	requireStatus(t, s.owner.upload(t, s.base+"/image", s.etag, 42, map[string]string{"X-CSRF-TOKEN": s.owner.csrf}), 200, &uploaded)
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/members/current/deletion-requests", "", nil), 202, nil)
	var outbox string
	if err := s.owner.store.Pool.QueryRow(t.Context(), `SELECT id::text FROM authentication_email_outbox WHERE kind='ACCOUNT_DELETION_CONFIRMATION'`).Scan(&outbox); err != nil {
		t.Fatal(err)
	}
	message, err := s.owner.service.PrepareAuthenticationEmail(t.Context(), outbox)
	if err != nil {
		t.Fatal(err)
	}
	action, err := url.Parse(message.ActionURL)
	if err != nil {
		t.Fatal(err)
	}
	body, err := json.Marshal(map[string]string{"token": actionCredentials(t, action).Get("token")})
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/members/current/deletion-requests/confirm", string(body), nil), 204, nil)
	var group domain.GroupResponse
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 200, &group)
	if group.OwnerId != s.memberID || group.MemberCount != 1 || group.ProfileImageUrl == nil {
		t.Fatal("group or photo was lost after account deletion")
	}
	image, err := url.Parse(*group.ProfileImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, s.member.send(t, "GET", image.RequestURI(), "", nil), 200, nil)
}

func TestGroupMembers_WhenCreatorLeaves_GroupRemainsForOtherMembers(t *testing.T) {
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
	requireStatus(t, s.owner.send(t, "GET", s.base, "", nil), 404, nil)
	var group domain.GroupResponse
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 200, &group)
	if group.MemberCount != 1 || group.OwnerId != s.memberID {
		t.Fatal("remaining member lost the group")
	}
}

func TestGroupMembers_WhenLastMemberLeaves_RemovesGroupButKeepsWishlist(t *testing.T) {
	s := newGroupScenario(t)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Conserved","occasion":"other"}`, nil), 201, &list)
	requireStatus(t, s.owner.send(t, "PUT", s.base+"/wishlists/"+list.Id, "", nil), 201, nil)
	var group domain.GroupResponse
	requireStatus(t, s.owner.upload(t, s.base+"/image", s.etag, 42, map[string]string{"X-CSRF-TOKEN": s.owner.csrf}), 200, &group)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
	requireStatus(t, s.owner.send(t, "GET", "/api/v1/wishlists/"+list.Id, "", nil), 200, nil)
	var queued int
	if err := s.owner.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM gift_image_deletion_outbox`).Scan(&queued); err != nil || queued != 1 {
		t.Fatal("group photo cleanup was not queued")
	}
}
