//go:build integration

package httpapi_test

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func TestMemberActivity_WhenJSONActionsSucceedOrFail_StoresSanitizedPayloadAndSeverity(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "payload@example.test")
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Café été","occasion":"other"}`, nil), 201, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"","occasion":"other","message":"Tentative rejetée"}`, nil), 400, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Malformed`, nil), 400, nil)
	f.administrator(t, "payload-admin@example.test")
	// Act
	var page domain.PaginatedResponse[application.ActivityEvent]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=CAFE%20ETE&logLevel=info", "", nil), 200, &page)
	// Assert
	if page.TotalCount != 1 || page.Items[0].LogLevel != "info" || page.Items[0].MemberID != member {
		t.Fatalf("wrong success page: %+v", page)
	}
	var payload map[string]any
	if err := json.Unmarshal(page.Items[0].Payload, &payload); err != nil {
		t.Fatal(err)
	}
	if payload["name"] != "Café été" {
		t.Fatalf("missing request data: %+v", payload)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=tentative%20REJETEE&logLevel=ERROR", "", nil), 200, &page)
	if page.TotalCount != 1 || page.Items[0].LogLevel != "error" {
		t.Fatalf("wrong failure page: %+v", page)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?logLevel=error", "", nil), 200, &page)
	if page.TotalCount != 2 {
		t.Fatalf("failed actions omitted: %+v", page)
	}
	var nullPayload bool
	for _, item := range page.Items {
		if len(item.Payload) == 0 || string(item.Payload) == "null" {
			nullPayload = true
		}
	}
	if !nullPayload {
		t.Fatal("malformed raw body must not be stored")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?logLevel=debug", "", nil), 400, nil)
}

func TestMemberActivity_WhenGroupSuggestionIsRecorded_OmitsPrivateIdeaContent(t *testing.T) {
	// Arrange
	s, _, collection := suggestionScenario(t)
	requireStatus(t, s.member.send(t, "POST", collection, `{"name":"Secret group idea","note":"Hidden beneficiary surprise"}`, nil), 201, nil)
	s.owner.administrator(t, "idea-payload-admin@example.test")
	// Act
	var page domain.PaginatedResponse[application.ActivityEvent]
	requireStatus(t, s.owner.send(t, "GET", "/api/v1/admin/member-activity-events?q=suggestions", "", nil), 200, &page)
	// Assert
	if page.TotalCount != 1 {
		t.Fatalf("missing suggestion event: %+v", page)
	}
	if !strings.Contains(string(page.Items[0].Payload), "privateGroupSuggestion") {
		t.Fatal("private idea payload was not omitted")
	}
	// Verify the security invariant in storage, not merely a filtered HTTP projection.
	var persisted int
	if err := s.owner.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM member_activity_events WHERE payload::text LIKE '%Secret group idea%' OR payload::text LIKE '%Hidden beneficiary surprise%'`).Scan(&persisted); err != nil {
		t.Fatal(err)
	}
	if persisted != 0 {
		t.Fatal("private group content was persisted")
	}
}

func TestMemberActivity_WhenPasswordChangeFails_MasksCredentialsBeforePersistence(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "credential-payload@example.test")
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/password", `{"currentPassword":"unlogged-current-secret","newPassword":"tiny"}`, nil), 400, nil)
	f.administrator(t, "credential-payload-admin@example.test")
	// Act
	var page domain.PaginatedResponse[application.ActivityEvent]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=password&logLevel=error", "", nil), 200, &page)
	// Assert
	if page.TotalCount != 1 {
		t.Fatalf("missing credential action: %+v", page)
	}
	var payload map[string]any
	if err := json.Unmarshal(page.Items[0].Payload, &payload); err != nil {
		t.Fatal(err)
	}
	if payload["currentPassword"] != "[REDACTED]" || payload["newPassword"] != "[REDACTED]" {
		t.Fatal("credential fields are not masked")
	}
	var persisted int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM member_activity_events WHERE payload::text LIKE '%unlogged-current-secret%' OR payload::text LIKE '%tiny%'`).Scan(&persisted); err != nil {
		t.Fatal(err)
	}
	if persisted != 0 {
		t.Fatal("credentials were persisted")
	}
}

func TestMemberActivity_WhenPagesAndActionsRecorded_AdminCanFilter(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "activity@example.test")
	if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET display_name='Élodie' WHERE id=$1`, member); err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "POST", "/api/v1/member-activity/page-visits", `{"page":"group-wishlist"}`, nil), 204, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/member-activity/page-visits", `{"page":"/private?secret=bad"}`, nil), 400, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Private value","occasion":"other"}`, nil), 201, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events", "", nil), 403, nil)
	f.administrator(t, "activity-admin@example.test")
	// Act
	var page domain.PaginatedResponse[application.ActivityEvent]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q="+url.QueryEscape("eLODIE"), "", nil), 200, &page)
	// Assert
	if page.TotalCount != 2 || len(page.Items) != 2 {
		t.Fatalf("unexpected page: %+v", page)
	}
	kinds := map[string]bool{}
	for _, item := range page.Items {
		if item.MemberID != member {
			t.Fatal("wrong member")
		}
		kinds[item.Kind] = true
		if item.Activity == "Private value" {
			t.Fatal("request data leaked")
		}
	}
	if !kinds["pageVisit"] || !kinds["action"] {
		t.Fatalf("missing activity: %+v", kinds)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=eloDIE&kind=pageVisit", "", nil), 200, &page)
	if page.TotalCount != 1 || page.Items[0].Activity != "Liste du groupe" {
		t.Fatalf("wrong filtered visits: %+v", page)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=LISTES&kind=action", "", nil), 200, &page)
	if page.TotalCount != 1 || page.Items[0].Activity != "Envoi · listes" {
		t.Fatalf("action filter failed: %+v", page)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=LISTE%20DU%20GROUPE", "", nil), 200, &page)
	if page.TotalCount != 1 {
		t.Fatalf("activity label filter failed: %+v", page)
	}
	if _, err := f.store.Pool.Exec(t.Context(), `UPDATE member_activity_events SET occurred_at=$2 WHERE member_id=$1 AND kind='pageVisit'`, member, f.service.Now().Add(-91*24*time.Hour)); err != nil {
		t.Fatal(err)
	}
	w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	defer w.Close()
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=elodie", "", nil), 200, &page)
	if page.TotalCount != 1 {
		t.Fatalf("expired activity not removed: %+v", page)
	}
	if _, err := f.store.Pool.Exec(t.Context(), `DELETE FROM users WHERE id=$1`, member); err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?q=elodie", "", nil), 200, &page)
	if page.TotalCount != 0 {
		t.Fatalf("deleted member activity retained: %+v", page)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?page=0", "", nil), 400, nil)
	if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET is_admin=false WHERE email='activity-admin@example.test'`); err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events", "", nil), 403, nil)
}
