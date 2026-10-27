//go:build integration

package httpapi_test

import (
	"context"
	"fmt"
	"net/http"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestGroupJoinLink_WhenUserHasConcurrentForeignKeyReference_DoesNotBlockEnrollment(t *testing.T) {
	// Arrange: an invitation may hold this reference while it owns the group lock.
	s := newGroupScenario(t)
	var link domain.GroupJoinLinkResponse
	requireStatus(t, s.owner.send(t, "POST", s.base+"/join-link", "", nil), 201, &link)
	reference, err := s.owner.store.Pool.Begin(t.Context())
	if err != nil {
		t.Fatal(err)
	}
	defer reference.Rollback(t.Context())
	var member string
	if err = reference.QueryRow(t.Context(), `SELECT id::text FROM users WHERE id=$1 FOR KEY SHARE`, s.foreignID).Scan(&member); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(t.Context(), 10*time.Second)
	defer cancel()
	// Act
	group, created, err := s.owner.service.JoinGroup(ctx, s.foreignID, link.Code)
	// Assert
	if err != nil || !created || group.Id != s.group.Id {
		t.Fatalf("joining blocked an unrelated foreign-key reference: %v", err)
	}
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 200, nil)
}

func TestGroupInvitations_WhenSentByAcceptedMember_RequireRecipientConsentAndPreserveOwnerControls(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var invitation domain.GroupInvitationResponse
	// Act
	created := s.member.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, s.foreignID), nil)
	location := created.Header.Get("Location")
	requireStatus(t, created, 201, &invitation)
	// Assert
	requireStatus(t, s.member.send(t, "GET", location, "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "GET", s.base+"/invitations", "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "DELETE", location, "", nil), 403, nil)
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, s.ownerID), nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "POST", "/api/v1/group-invitations/"+invitation.Id+"/accept", "", nil), 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 200, nil)
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.memberID, "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, s.ownerID), nil), 404, nil)
}

func TestGroupJoinLink_WhenSharedByMember_JoinsWithoutApprovalAndConsumesPendingInvitation(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	invitation := s.invite(t, s.foreignID)
	path := s.base + "/join-link"
	requireStatus(t, s.member.send(t, "GET", path, "", nil), 404, nil)
	var link domain.GroupJoinLinkResponse
	created := s.member.send(t, "POST", path, "", nil)
	location, etag := created.Header.Get("Location"), created.Header.Get("ETag")
	requireStatus(t, created, 201, &link)
	if location != path || etag == "" || link.GroupId != s.group.Id || len(link.Code) != 16 || link.JoinUrl != "http://localhost:5173/groups/join/"+s.group.Id+"#"+link.Code {
		t.Fatal("invalid private join-link contract")
	}
	var hash []byte
	var protected string
	if err := s.owner.store.Pool.QueryRow(t.Context(), `SELECT code_hash,protected_code FROM group_join_links WHERE group_id=$1`, s.group.Id).Scan(&hash, &protected); err != nil {
		t.Fatal(err)
	}
	if !security.VerifyToken(link.Code, hash) || strings.Contains(protected, link.Code) {
		t.Fatal("join credential protection invariant violated")
	}
	requireStatus(t, s.member.send(t, "POST", path, "", nil), 409, nil)
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
	// Act
	formatted := strings.ToLower(link.Code[:4] + "-" + link.Code[4:8] + "-" + link.Code[8:12] + "-" + link.Code[12:])
	joined := s.foreign.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": formatted})
	joinedLocation := joined.Header.Get("Location")
	var group domain.GroupResponse
	requireStatus(t, joined, 201, &group)
	// Assert
	if group.Id != s.group.Id || group.MemberCount != 3 || joinedLocation != s.base {
		t.Fatal("joining did not return the enrolled group")
	}
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", "/api/v1/group-invitations/"+invitation.Id, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": link.Code}), 200, &group)
	if group.MemberCount != 3 {
		t.Fatal("repeated join duplicated membership")
	}
	var read domain.GroupJoinLinkResponse
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 200, &read)
	if read.Code != link.Code {
		t.Fatal("members did not receive the same join credential")
	}
}

func TestGroupJoinLink_WhenConcurrentRequestsRedeemSameCode_CreatesOneMembership(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	var link domain.GroupJoinLinkResponse
	requireStatus(t, s.owner.send(t, "POST", s.base+"/join-link", "", nil), 201, &link)
	type outcome struct {
		response *http.Response
		err      error
	}
	results := make(chan outcome, 2)
	start := make(chan struct{})
	// Act
	for i := 0; i < 2; i++ {
		req, err := http.NewRequestWithContext(t.Context(), "POST", s.owner.server.URL+"/api/v1/groups/join", nil)
		if err != nil {
			t.Fatal(err)
		}
		req.Header.Set("Authorization", "Bearer "+s.foreign.access)
		req.Header.Set("X-CSRF-TOKEN", s.foreign.csrf)
		req.Header.Set("X-MonKado-Share-Token", link.Code)
		go func() { <-start; response, err := s.foreign.client.Do(req); results <- outcome{response, err} }()
	}
	close(start)
	// Assert
	statuses := map[int]int{}
	for i := 0; i < 2; i++ {
		select {
		case result := <-results:
			if result.err != nil {
				t.Fatal(result.err)
			}
			statuses[result.response.StatusCode]++
			result.response.Body.Close()
		case <-t.Context().Done():
			t.Fatal(t.Context().Err())
		}
	}
	if statuses[201] != 1 || statuses[200] != 1 {
		t.Fatalf("unexpected concurrent join statuses: %v", statuses)
	}
	var members domain.PaginatedResponse[domain.GroupMemberResponse]
	requireStatus(t, s.foreign.send(t, "GET", s.base+"/members", "", nil), 200, &members)
	if members.TotalCount != 2 {
		t.Fatal("joining duplicated the membership")
	}
}

func TestGroupJoinLink_WhenRevoked_InvalidatesOldCodesAndPreservesAcceptedMemberships(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	path := s.base + "/join-link"
	var first, second domain.GroupJoinLinkResponse
	created := s.owner.send(t, "POST", path, "", nil)
	oldVersion := created.Header.Get("ETag")
	requireStatus(t, created, 201, &first)
	// Act / Assert
	requireStatus(t, s.member.send(t, "DELETE", path, "", map[string]string{"If-Match": oldVersion}), 403, nil)
	requireStatus(t, s.owner.send(t, "DELETE", path, "", nil), 428, nil)
	requireStatus(t, s.owner.send(t, "DELETE", path, "", map[string]string{"If-Match": "bad"}), 400, nil)
	requireStatus(t, s.owner.send(t, "DELETE", path, "", map[string]string{"If-Match": oldVersion}), 204, nil)
	requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": first.Code}), 404, nil)
	requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 200, nil)
	requireStatus(t, s.member.send(t, "POST", path, "", nil), 201, &second)
	if first.Code == second.Code {
		t.Fatal("reactivation reused a revoked code")
	}
	requireStatus(t, s.owner.send(t, "DELETE", path, "", map[string]string{"If-Match": oldVersion}), 412, nil)
	requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": second.Code}), 201, nil)
}

func TestGroupJoinLink_WhenInputAuthenticationOrCsrfInvalid_DoesNotEnroll(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	var link domain.GroupJoinLinkResponse
	requireStatus(t, s.owner.send(t, "POST", s.base+"/join-link", "", nil), 201, &link)
	// Act / Assert
	for _, code := range []string{"", "bad", strings.Repeat("A", 33), strings.Repeat("1", 16)} {
		t.Run(fmt.Sprintf("length-%d", len(code)), func(t *testing.T) {
			var failure domain.ErrorResponse
			requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": code}), 400, &failure)
			if failure.ErrorCode == nil || *failure.ErrorCode != domain.RequestValidationError || len(failure.ValidationErrors) != 1 || failure.ValidationErrors[0].PropertyName != "X-MonKado-Share-Token" {
				t.Fatal("invalid code validation contract")
			}
		})
	}
	requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": strings.Repeat("A", 16)}), 404, nil)
	visitor := s.owner.visitor(t)
	requireStatus(t, visitor.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": link.Code}), 401, nil)
	s.foreign.csrf = ""
	requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": link.Code}), 400, nil)
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
}

func TestGroupJoinLink_WhenMemberAtGroupLimit_RejectsNewMembershipButAllowsExistingGroup(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var link domain.GroupJoinLinkResponse
	requireStatus(t, s.owner.send(t, "POST", s.base+"/join-link", "", nil), 201, &link)
	for i := 0; i < 50; i++ {
		requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups", fmt.Sprintf(`{"name":"Group %d"}`, i), nil), 201, nil)
	}
	// Act / Assert
	requireStatus(t, s.foreign.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": link.Code}), 409, nil)
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
	for i := 0; i < 49; i++ {
		requireStatus(t, s.member.send(t, "POST", "/api/v1/groups", fmt.Sprintf(`{"name":"Existing %d"}`, i), nil), 201, nil)
	}
	requireStatus(t, s.member.send(t, "POST", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": link.Code}), 200, nil)
}
