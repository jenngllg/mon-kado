//go:build integration

package httpapi_test

import (
	"fmt"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

type groupScenario struct {
	owner, member, foreign       *fixture
	ownerID, memberID, foreignID string
	group                        domain.GroupResponse
	base, etag                   string
}

func TestGroupInvitations_WhenListingReceived_ExposesOnlyActiveRecipientInvitations(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	invitation := s.invite(t, s.memberID)
	s.invite(t, s.foreignID)
	path := "/api/v1/group-invitations"
	// Act
	var received domain.PaginatedResponse[domain.GroupInvitationResponse]
	requireStatus(t, s.member.send(t, "GET", path, "", nil), 200, &received)
	// Assert: pending consent permits reading the invitation, never the group.
	if received.TotalCount != 1 || len(received.Items) != 1 || received.Items[0].Id != invitation.Id || received.Items[0].MemberId != s.memberID {
		t.Fatal("received invitations include another recipient or omit the current one")
	}
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "GET", path, "", nil), 200, &received)
	if received.TotalCount != 0 || received.Items == nil || len(received.Items) != 0 {
		t.Fatal("sending an invitation made it a received invitation")
	}
	requireStatus(t, s.member.send(t, "GET", path+"?page=2&pageSize=1", "", nil), 200, &received)
	if received.CurrentPage != 2 || received.TotalCount != 1 || len(received.Items) != 0 || !received.HasPreviousPage {
		t.Fatal("received invitation pagination differs")
	}
	requireStatus(t, s.member.send(t, "GET", path+"?pageSize=0", "", nil), 400, nil)
	now := s.owner.service.Now()
	s.owner.service.Now = func() time.Time { return now.Add(15 * 24 * time.Hour) }
	received, err := s.owner.service.GroupInvitations(t.Context(), s.memberID, "", 1, 20)
	if err != nil || received.TotalCount != 0 || received.Items == nil || len(received.Items) != 0 {
		t.Fatal("expired invitations remain in the received collection")
	}
}

func TestGroups_WhenSharingAcrossGroups_UsesOneReservationAndRevokesMemberOwnedGrants(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	s.accept(t, s.foreign, s.foreignID)
	var second domain.GroupResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/groups", `{"name":"Friends"}`, nil), 201, &second)
	secondBase := "/api/v1/groups/" + second.Id
	secondScenario := s
	secondScenario.base = secondBase
	secondScenario.accept(t, s.member, s.memberID)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"One original list","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"One original wish"}`, nil), 201, &wish)
	first := s.base + "/wishlists/" + list.Id
	secondPath := secondBase + "/wishlists/" + list.Id
	requireStatus(t, s.owner.send(t, "PUT", first, "", nil), 201, nil)
	requireStatus(t, s.owner.send(t, "PUT", secondPath, "", nil), 201, nil)
	// Act
	created := s.member.send(t, "PUT", first+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, nil)
	etag := created.Header.Get("ETag")
	var reservation domain.GiftReservationResponse
	requireStatus(t, created, 201, &reservation)
	var same domain.GiftReservationResponse
	requireStatus(t, s.member.send(t, "GET", secondPath+"/wishes/"+wish.Id+"/reservations/current", "", nil), 200, &same)
	if same.Id != reservation.Id {
		t.Fatal("sharing duplicated a reservation")
	}
	requireStatus(t, s.foreign.send(t, "PUT", first+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, nil), 409, nil)
	// Assert: removing one grant retains only independently valid group access.
	requireStatus(t, s.owner.send(t, "DELETE", first, "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "GET", first, "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", secondPath, "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "DELETE", secondPath+"/wishes/"+wish.Id+"/reservations/current", "", map[string]string{"If-Match": etag}), 204, nil)
	var personal domain.WishlistResponse
	requireStatus(t, s.member.send(t, "POST", "/api/v1/wishlists", `{"name":"Member list","occasion":"other"}`, nil), 201, &personal)
	personalPath := s.base + "/wishlists/" + personal.Id
	requireStatus(t, s.member.send(t, "PUT", personalPath, "", nil), 201, nil)
	requireStatus(t, s.owner.send(t, "GET", personalPath, "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
	requireStatus(t, s.owner.send(t, "GET", personalPath, "", nil), 404, nil)
	s.accept(t, s.member, s.memberID)
	requireStatus(t, s.owner.send(t, "GET", personalPath, "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", "/api/v1/wishlists/"+personal.Id, "", nil), 200, nil)
}

func TestGroups_WhenImageAccessIsRevokedOrHidden_InvalidatesGroupBoundGrants(t *testing.T) {
	for _, action := range []string{"unshare", "leave", "remove", "archive", "suspend", "deleteGroup"} {
		t.Run(action, func(t *testing.T) {
			// Arrange
			s := newGroupScenario(t)
			s.accept(t, s.member, s.memberID)
			var list domain.WishlistResponse
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Private pictures","occasion":"other"}`, nil), 201, &list)
			own := "/api/v1/wishlists/" + list.Id
			var wish domain.WishResponse
			created := s.owner.send(t, "POST", own+"/wishes", `{"name":"Photo"}`, nil)
			etag := created.Header.Get("ETag")
			requireStatus(t, created, 201, &wish)
			requireStatus(t, s.owner.upload(t, own+"/wishes/"+wish.Id+"/image", etag, 42), 200, &wish)
			base := s.base + "/wishlists/" + list.Id
			requireStatus(t, s.owner.send(t, "PUT", base, "", nil), 201, nil)
			var detail domain.SharedWishDetailResponse
			requireStatus(t, s.member.send(t, "GET", base+"/wishes/"+wish.Id, "", nil), 200, &detail)
			if detail.ImageUrl == nil {
				t.Fatal("missing group image")
			}
			grant, err := url.Parse(*detail.ImageUrl)
			if err != nil {
				t.Fatal(err)
			}
			requireStatus(t, s.member.send(t, "GET", grant.RequestURI(), "", nil), 200, nil)
			requireStatus(t, s.member.send(t, "GET", strings.Replace(grant.RequestURI(), s.group.Id, security.NewID(), 1), "", nil), 404, nil)
			// Act
			switch action {
			case "unshare":
				requireStatus(t, s.owner.send(t, "DELETE", base, "", nil), 204, nil)
			case "leave":
				requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
			case "remove":
				requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.memberID, "", nil), 204, nil)
			case "archive":
				read := s.owner.send(t, "GET", own, "", nil)
				version := read.Header.Get("ETag")
				requireStatus(t, read, 200, nil)
				requireStatus(t, s.owner.send(t, "PATCH", own, `{"isArchived":true}`, map[string]string{"If-Match": version}), 200, nil)
			case "suspend":
				// The technical moderation invariant must apply to every access path.
				if _, err = s.owner.store.Pool.Exec(t.Context(), `UPDATE wishlists SET is_suspended=true,suspension_reason='Test moderation',suspended_at=now() WHERE id=$1`, list.Id); err != nil {
					t.Fatal(err)
				}
			case "deleteGroup":
				requireStatus(t, s.owner.send(t, "DELETE", s.base, "", map[string]string{"If-Match": s.etag}), 204, nil)
			}
			// Assert: grants are not a cached membership or a public capability.
			requireStatus(t, s.member.send(t, "GET", grant.RequestURI(), "", nil), 404, nil)
			requireStatus(t, s.member.send(t, "GET", base+"/wishes/"+wish.Id, "", nil), 404, nil)
			requireStatus(t, s.owner.send(t, "GET", own, "", nil), 200, nil)
		})
	}
}

func TestGroups_WhenMembershipLimitIsReached_RejectsCreationAndKeepsInvitationPending(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	invitation := s.invite(t, s.memberID)
	for index := 0; index < 50; index++ {
		requireStatus(t, s.member.send(t, "POST", "/api/v1/groups", fmt.Sprintf(`{"name":"Group %d"}`, index), nil), 201, nil)
	}
	// Act / Assert
	requireStatus(t, s.member.send(t, "POST", "/api/v1/groups", `{"name":"Too many"}`, nil), 409, nil)
	requireStatus(t, s.member.send(t, "POST", "/api/v1/group-invitations/"+invitation.Id+"/accept", "", nil), 409, nil)
	requireStatus(t, s.member.send(t, "GET", "/api/v1/group-invitations/"+invitation.Id, "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 404, nil)
}

func newGroupScenario(t *testing.T) groupScenario {
	t.Helper()
	f := newFixture(t)
	ownerID := f.member(t, "group-owner@example.test")
	member := f.visitor(t)
	memberID := member.member(t, "group-member@example.test")
	foreign := f.visitor(t)
	foreignID := foreign.member(t, "group-foreign@example.test")
	var group domain.GroupResponse
	created := f.send(t, "POST", "/api/v1/groups", `{"name":"Family"}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &group)
	return groupScenario{f, member, foreign, ownerID, memberID, foreignID, group, "/api/v1/groups/" + group.Id, etag}
}

func (s groupScenario) invite(t *testing.T, target string) domain.GroupInvitationResponse {
	t.Helper()
	var invitation domain.GroupInvitationResponse
	created := s.owner.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, target), nil)
	location := created.Header.Get("Location")
	requireStatus(t, created, 201, &invitation)
	if location != s.base+"/invitations/"+invitation.Id {
		t.Fatal("invitation location mismatch")
	}
	requireStatus(t, s.owner.send(t, "GET", location, "", nil), 200, nil)
	return invitation
}

func (s groupScenario) accept(t *testing.T, f *fixture, target string) {
	t.Helper()
	invitation := s.invite(t, target)
	requireStatus(t, f.send(t, "POST", "/api/v1/group-invitations/"+invitation.Id+"/accept", "", nil), 200, nil)
}

func TestGroups_WhenCreatingInvitingAcceptingAndLeaving_PreservesPrivateLifecycle(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	if s.group.MemberCount != 1 || s.group.OwnerId != s.ownerID || s.group.UpdatedAt != nil || s.etag == "" {
		t.Fatal("invalid new group")
	}
	var groups domain.PaginatedResponse[domain.GroupResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/groups", "", nil), 200, &groups)
	if groups.Items == nil || groups.TotalPages != 0 || groups.HasNextPage || groups.HasPreviousPage {
		t.Fatal("invalid empty group page")
	}
	// Act / Assert: a pending invitation is not access.
	invitation := s.invite(t, s.memberID)
	target := "/api/v1/group-invitations/" + invitation.Id
	requireStatus(t, s.member.send(t, "GET", target, "", nil), 200, nil)
	for _, path := range []string{s.base, s.base + "/members", s.base + "/wishlists", s.base + "/invitations"} {
		requireStatus(t, s.member.send(t, "GET", path, "", nil), 404, nil)
	}
	requireStatus(t, s.foreign.send(t, "GET", target, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "POST", target+"/accept", "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, s.memberID), nil), 409, nil)
	requireStatus(t, s.member.send(t, "POST", target+"/accept", "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "POST", target+"/accept", "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", "/api/v1/groups?page=2147483647", "", nil), 200, &groups)
	if groups.CurrentPage != 2147483647 || groups.TotalCount != 1 || len(groups.Items) != 0 || !groups.HasPreviousPage {
		t.Fatal("pagination is inconsistent")
	}
	var members domain.PaginatedResponse[domain.GroupMemberResponse]
	requireStatus(t, s.member.send(t, "GET", s.base+"/members", "", nil), 200, &members)
	if len(members.Items) != 2 {
		t.Fatal("accepted membership was not persisted")
	}
	requireStatus(t, s.member.send(t, "GET", s.base+"/invitations", "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/"+s.ownerID, "", nil), 403, nil)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/current", "", nil), 409, nil)
	requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.memberID, "", nil), 404, nil)
}

func TestGroups_WhenRenamingRevokingDecliningOrExpiring_EnforcesOwnershipVersionsAndConsent(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	// Act / Assert
	for _, method := range []string{"PUT", "DELETE"} {
		requireStatus(t, s.owner.send(t, method, s.base, `{"name":"Renamed"}`, nil), 428, nil)
		requireStatus(t, s.owner.send(t, method, s.base, `{"name":"Renamed"}`, map[string]string{"If-Match": "bad"}), 400, nil)
		requireStatus(t, s.member.send(t, method, s.base, `{"name":"Renamed"}`, map[string]string{"If-Match": s.etag}), 403, nil)
		requireStatus(t, s.foreign.send(t, method, s.base, `{"name":"Renamed"}`, map[string]string{"If-Match": s.etag}), 404, nil)
	}
	renamed := s.owner.send(t, "PUT", s.base, `{"name":" Renamed "}`, map[string]string{"If-Match": s.etag})
	next := renamed.Header.Get("ETag")
	var group domain.GroupResponse
	requireStatus(t, renamed, 200, &group)
	if group.Name != "Renamed" || group.UpdatedAt == nil {
		t.Fatal("rename not persisted")
	}
	requireStatus(t, s.owner.send(t, "PUT", s.base, `{"name":"Lost update"}`, map[string]string{"If-Match": s.etag}), 412, nil)
	for _, action := range []string{"decline", "revoke", "expire"} {
		invitation := s.invite(t, s.foreignID)
		received := "/api/v1/group-invitations/" + invitation.Id
		switch action {
		case "decline":
			requireStatus(t, s.foreign.send(t, "DELETE", received, "", nil), 204, nil)
		case "revoke":
			requireStatus(t, s.owner.send(t, "DELETE", s.base+"/invitations/"+invitation.Id, "", nil), 204, nil)
		case "expire":
			now := s.owner.service.Now()
			s.owner.service.Now = func() time.Time { return now.Add(15 * 24 * time.Hour) }
			_, err := s.owner.service.AnswerGroupInvitation(t.Context(), s.foreignID, invitation.Id, true)
			if failure, ok := err.(*domain.Error); !ok || failure.Status != 404 || failure.Code != domain.GroupInvitationNotFound {
				t.Fatalf("expired invitation accepted: %v", err)
			}
			s.owner.service.Now = time.Now
			continue
		}
		requireStatus(t, s.foreign.send(t, "POST", received+"/accept", "", nil), 404, nil)
		requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
	}
	requireStatus(t, s.owner.send(t, "DELETE", s.base, "", map[string]string{"If-Match": next}), 204, nil)
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 404, nil)
}

func TestGroups_WhenSharingAndReserving_KeepsListsPrivateAndUsesGlobalReservations(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Private birthday","occasion":"birthday"}`, nil), 201, &list)
	own := "/api/v1/wishlists/" + list.Id
	var wish domain.WishResponse
	requireStatus(t, s.owner.send(t, "POST", own+"/wishes", `{"name":"One gift","quantity":1}`, nil), 201, &wish)
	groupList := s.base + "/wishlists/" + list.Id
	var summaries domain.PaginatedResponse[domain.GroupWishlistResponse]
	requireStatus(t, s.member.send(t, "GET", s.base+"/wishlists", "", nil), 200, &summaries)
	if summaries.TotalCount != 0 {
		t.Fatal("creating a group shared a private list")
	}
	requireStatus(t, s.member.send(t, "GET", groupList, "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "PUT", groupList, "", nil), 404, nil)
	// Act
	requireStatus(t, s.owner.send(t, "PUT", groupList, "", nil), 201, nil)
	requireStatus(t, s.owner.send(t, "PUT", groupList, "", nil), 409, nil)
	requireStatus(t, s.owner.send(t, "GET", own+"/share-link", "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "GET", groupList, "", nil), 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", groupList, "", nil), 404, nil)
	var shared domain.SharedWishlistResponse
	requireStatus(t, s.member.send(t, "GET", groupList, "", nil), 200, &shared)
	if shared.CanSubscribe || shared.CurrentParticipant != nil || len(shared.Wishes) != 1 {
		t.Fatal("read enrolled a member or minted a follow capability")
	}
	reservation := groupList + "/wishes/" + wish.Id + "/reservations/current"
	requireStatus(t, s.member.send(t, "GET", reservation, "", nil), 404, nil)
	var saved domain.GiftReservationResponse
	created := s.member.send(t, "PUT", reservation, `{"quantity":1}`, nil)
	tag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &saved)
	requireStatus(t, s.member.send(t, "PUT", reservation, `{"quantity":1}`, nil), 428, nil)
	requireStatus(t, s.member.send(t, "PUT", reservation, `{"quantity":1}`, map[string]string{"If-Match": `"00000001"`}), 412, nil)
	requireStatus(t, s.member.send(t, "GET", groupList+"/wishes/"+security.NewID(), "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "PUT", own+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, nil), 409, nil)
	// Assert: history navigates through live membership and never invents public sharing.
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if len(history.Items) != 1 || history.Items[0].GroupWishPath == nil || history.Items[0].ShareUrl != nil || history.Items[0].GroupId == nil {
		t.Fatal("history did not preserve group-only access")
	}
	requireStatus(t, s.owner.send(t, "POST", own+"/share-link", "", nil), 201, nil)
	requireStatus(t, s.owner.send(t, "DELETE", groupList, "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "GET", groupList, "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "DELETE", reservation, "", map[string]string{"If-Match": tag}), 404, nil)
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if history.Items[0].GroupWishPath != nil || history.Items[0].ShareUrl != nil || history.Items[0].OwnerId != nil || history.Items[0].InaccessibilityReason == nil {
		t.Fatal("history escalated withdrawn group access to a public secret")
	}
	requireStatus(t, s.owner.send(t, "PUT", groupList, "", nil), 201, nil)
	requireStatus(t, s.member.send(t, "DELETE", reservation, "", map[string]string{"If-Match": tag}), 204, nil)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.memberID, "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "GET", groupList, "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "GET", own, "", nil), 200, nil)
}

func TestGroups_WhenInputIdentityOrCsrfIsInvalid_RejectsWithoutMutations(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	// Act / Assert
	for _, body := range []string{`{}`, `{"name":null}`, `{"name":" "}`, `{"name":"line\nbreak"}`, `{"name":"` + strings.Repeat("x", 101) + `"}`} {
		requireStatus(t, s.owner.send(t, "POST", "/api/v1/groups", body, nil), 400, nil)
	}
	for _, body := range []string{`{}`, `{"memberId":null}`, `{"memberId":"bad"}`} {
		requireStatus(t, s.owner.send(t, "POST", s.base+"/invitations", body, nil), 400, nil)
	}
	requireStatus(t, s.owner.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, security.NewID()), nil), 404, nil)
	requireStatus(t, s.owner.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, s.ownerID), nil), 409, nil)
	for _, path := range []string{"/api/v1/groups/bad", "/api/v1/group-invitations/bad"} {
		requireStatus(t, s.owner.send(t, "GET", path, "", nil), 404, nil)
	}
	for _, query := range []string{"?page=0", "?pageSize=101", "?page="} {
		requireStatus(t, s.owner.send(t, "GET", "/api/v1/groups"+query, "", nil), 400, nil)
	}
	for _, path := range []string{s.base + "/members", s.base + "/invitations", s.base + "/wishlists", "/api/v1/group-invitations"} {
		requireStatus(t, s.owner.send(t, "GET", path+"?pageSize=0", "", nil), 400, nil)
	}
	requireStatus(t, s.owner.send(t, "PUT", s.base, `{"name":""}`, map[string]string{"If-Match": s.etag}), 400, nil)
	reservation := s.base + "/wishlists/" + security.NewID() + "/wishes/" + security.NewID() + "/reservations/current"
	requireStatus(t, s.owner.send(t, "DELETE", reservation, "", nil), 428, nil)
	requireStatus(t, s.owner.send(t, "PUT", reservation, `{"quantity":1}`, map[string]string{"If-Match": "bad"}), 400, nil)
	requireStatus(t, s.owner.send(t, "PUT", reservation, `{"quantity":0}`, nil), 400, nil)
	requireStatus(t, s.owner.send(t, "PUT", s.base+"/image", "not a multipart image", map[string]string{"If-Match": s.etag}), 415, nil)
	visitor := s.owner.visitor(t)
	requireStatus(t, visitor.send(t, "GET", "/api/v1/groups", "", nil), 401, nil)
	s.owner.csrf = ""
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/groups", `{"name":"No CSRF"}`, nil), 400, nil)
	requireStatus(t, s.owner.send(t, "DELETE", s.base, "", map[string]string{"If-Match": s.etag}), 400, nil)
}
