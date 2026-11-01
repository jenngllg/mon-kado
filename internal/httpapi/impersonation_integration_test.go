//go:build integration

package httpapi_test

import (
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"testing"
	"time"
)

func TestImpersonation_WhenBusinessDataChanges_PersistsForTargetAndAuditsAdministrator(t *testing.T) {
	// Arrange
	f := newFixture(t)
	target := f.member(t, "write-simulation-member@example.test")
	targetAccess := f.access
	admin := f.administrator(t, "write-simulation-admin@example.test")
	adminAccess := f.access
	var sim application.ImpersonationResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+target+"/impersonations", "", nil), 201, &sim)
	headers := map[string]string{"X-MonKado-Impersonation": sim.ID}
	var resumed application.ImpersonationResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/impersonations/"+sim.ID, "", nil), 200, &resumed)
	if resumed.ID != sim.ID || resumed.User.Id != target {
		t.Fatal("delegation could not be restored through the real administrator session")
	}
	// Act: create, update and delete with the effective user's normal ETags.
	var list domain.WishlistResponse
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Created during simulation","occasion":"other"}`, headers)
	tag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &list)
	path := "/api/v1/wishlists/" + list.Id
	headers["If-Match"] = tag
	updated := f.send(t, "PUT", path, `{"name":"Updated during simulation","occasion":"other"}`, headers)
	newTag := updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &list)
	requireStatus(t, f.send(t, "PUT", path, `{"name":"Stale write","occasion":"other"}`, headers), 412, nil)
	delete(headers, "If-Match")
	var group domain.GroupResponse
	groupCreated := f.send(t, "POST", "/api/v1/groups", `{"name":"Delegated group"}`, headers)
	groupTag := groupCreated.Header.Get("ETag")
	requireStatus(t, groupCreated, 201, &group)
	groupPath := "/api/v1/groups/" + group.Id
	headers["If-Match"] = groupTag
	requireStatus(t, f.send(t, "PUT", groupPath, `{"name":"Updated delegated group"}`, headers), 200, &group)
	delete(headers, "If-Match")
	// Assert persistence through the original member's public contract, without the delegation header.
	f.access = targetAccess
	var stored domain.WishlistResponse
	requireStatus(t, f.send(t, "GET", path, "", nil), 200, &stored)
	if stored.Name != "Updated during simulation" {
		t.Fatal("delegated mutation was not persisted for the target")
	}
	requireStatus(t, f.send(t, "GET", groupPath, "", nil), 200, &group)
	if group.Name != "Updated delegated group" || group.OwnerId != target {
		t.Fatal("group writes did not persist with target ownership")
	}
	f.access = adminAccess
	requireStatus(t, f.send(t, "GET", path, "", nil), 404, nil)
	headers["If-Match"] = newTag
	requireStatus(t, f.send(t, "DELETE", path, "", headers), 204, nil)
	delete(headers, "If-Match")
	// Security operations remain unavailable even with the target identity.
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/password", `{"currentPassword":"irrelevant","newPassword":"Never applied password"}`, headers), 403, nil)
	var page domain.PaginatedResponse[application.ActivityEvent]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=Updated%20during%20simulation", "", nil), 200, &page)
	if page.TotalCount != 1 {
		t.Fatalf("missing mutation outcome: %+v", page)
	}
	event := page.Items[0]
	if event.MemberID != target || event.ActorMemberID == nil || *event.ActorMemberID != admin || !event.IsImpersonated {
		t.Fatal("delegated mutation lost real actor attribution")
	}
	var payload map[string]any
	if err := json.Unmarshal(event.Payload, &payload); err != nil {
		t.Fatal(err)
	}
	if payload["name"] != "Updated during simulation" {
		t.Fatal("mutation payload missing")
	}
	f.access = targetAccess
	requireStatus(t, f.send(t, "GET", path, "", nil), 404, nil)
}

func TestImpersonation_WhenAdministratorSimulatesMember_SeparatesIdentityAndActor(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "simulation-member@example.test")
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Target list","occasion":"other"}`, nil), 201, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+member+"/impersonations", "", nil), 403, nil)
	admin := f.administrator(t, "simulation-admin@example.test")
	var sim application.ImpersonationResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+member+"/impersonations", "", nil), 201, &sim)
	headers := map[string]string{"X-MonKado-Impersonation": sim.ID}
	// Act / Assert
	if sim.User.Id != member || sim.User.IsAdmin {
		t.Fatal("incorrect simulated identity")
	}
	var lists []domain.WishlistResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", headers), 200, &lists)
	if len(lists) != 1 || lists[0].Name != "Target list" {
		t.Fatal("did not apply target resource authorization")
	}
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Real delegated write","occasion":"other"}`, headers), 201, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events", "", headers), 403, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/member-activity/page-visits", `{"page":"lists"}`, headers), 204, nil)
	var activity domain.PaginatedResponse[application.ActivityEvent]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events", "", nil), 200, &activity)
	var found bool
	for _, event := range activity.Items {
		if event.MemberID == member && event.IsImpersonated {
			found = true
			if event.ActorMemberID == nil || *event.ActorMemberID != admin {
				t.Fatal("real actor lost")
			}
		}
	}
	if !found {
		t.Fatal("simulation activity missing")
	}
	requireStatus(t, f.send(t, "DELETE", "/api/v1/admin/impersonations/"+sim.ID, "", nil), 204, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", headers), 403, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/impersonations/"+sim.ID, "", nil), 403, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &lists)
	if len(lists) != 0 {
		t.Fatal("administrator identity was not retained")
	}
}

func TestImpersonation_WhenLeaseExpiresOrRoleIsRevoked_RejectsNextRequest(t *testing.T) {
	for _, scenario := range []string{"expiry", "role", "otherSession"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			member := f.member(t, "lease-member@example.test")
			admin := f.administrator(t, "lease-admin@example.test")
			var sim application.ImpersonationResponse
			requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+member+"/impersonations", "", nil), 201, &sim)
			// Act
			switch scenario {
			case "expiry":
				now := f.service.Now()
				f.service.Now = func() time.Time { return now.Add(16 * time.Minute) }
			case "role":
				if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET is_admin=false WHERE id=$1`, admin); err != nil {
					t.Fatal(err)
				}
			case "otherSession":
				f.administrator(t, "other-simulation-admin@example.test")
			}
			// Assert (access tokens may themselves expire first).
			response := f.send(t, "GET", "/api/v1/wishlists", "", map[string]string{"X-MonKado-Impersonation": sim.ID})
			defer response.Body.Close()
			if response.StatusCode != 401 && response.StatusCode != 403 {
				t.Fatalf("delegation retained access: %d", response.StatusCode)
			}
		})
	}
}

func TestImpersonation_WhenTargetOwnsGroupList_PreservesSurprisePrivacy(t *testing.T) {
	// Arrange
	s, _, collection := suggestionScenario(t)
	requireStatus(t, s.member.send(t, "POST", collection, `{"name":"Private surprise"}`, nil), 201, nil)
	target := s.ownerID
	s.owner.administrator(t, "surprise-simulation-admin@example.test")
	var sim application.ImpersonationResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/admin/members/"+target+"/impersonations", "", nil), 201, &sim)
	// Act / Assert
	requireStatus(t, s.owner.send(t, "GET", collection, "", map[string]string{"X-MonKado-Impersonation": sim.ID}), 404, nil)
}

func TestImpersonation_WhenAuditStoreCannotRecordAttempt_DoesNotRunMutation(t *testing.T) {
	// Arrange
	f := newFixture(t)
	target := f.member(t, "audit-write-member@example.test")
	memberAccess := f.access
	f.administrator(t, "audit-write-admin@example.test")
	var sim application.ImpersonationResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+target+"/impersonations", "", nil), 201, &sim)
	// A database trigger models an unavailable audit write without disconnecting the business store.
	_, err := f.store.Pool.Exec(t.Context(), `CREATE FUNCTION reject_delegated_attempt() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.is_impersonated THEN RAISE EXCEPTION 'Audit unavailable'; END IF; RETURN NEW; END; $$; CREATE TRIGGER test_reject_delegated_attempt BEFORE INSERT ON member_activity_events FOR EACH ROW EXECUTE FUNCTION reject_delegated_attempt();`)
	if err != nil {
		t.Fatal(err)
	}
	defer func() {
		if _, err := f.store.Pool.Exec(t.Context(), `DROP TRIGGER test_reject_delegated_attempt ON member_activity_events; DROP FUNCTION reject_delegated_attempt()`); err != nil {
			t.Fatal(err)
		}
	}()
	// Act
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Must not persist","occasion":"other"}`, map[string]string{"X-MonKado-Impersonation": sim.ID}), 503, nil)
	// Assert via the original member's public contract.
	f.access = memberAccess
	var lists []domain.WishlistResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &lists)
	if len(lists) != 0 {
		t.Fatal("mutation ran without durable actor attribution")
	}
}

func TestImpersonation_WhenAdministratorRefreshesAfterFormerDeadline_RemainsActiveUntilStopped(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "unlimited-simulation-member@example.test")
	f.administrator(t, "unlimited-simulation-admin@example.test")
	var sim application.ImpersonationResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+member+"/impersonations", "", nil), 201, &sim)
	if sim.ExpiresAt != nil {
		t.Fatal("simulation must not have its own deadline")
	}
	now := f.service.Now()
	f.service.Now = func() time.Time { return now.Add(2 * time.Hour) }
	// Act: renew the administrator token through the existing session cookie.
	f.access = ""
	f.csrfToken(t)
	var token domain.AccessTokenResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions/refresh", "", nil), 200, &token)
	f.access = token.AccessToken
	f.csrfToken(t)
	headers := map[string]string{"X-MonKado-Impersonation": sim.ID}
	// Assert
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", headers), 200, nil)
	requireStatus(t, f.send(t, "DELETE", "/api/v1/admin/impersonations/"+sim.ID, "", nil), 204, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", headers), 403, nil)
}
