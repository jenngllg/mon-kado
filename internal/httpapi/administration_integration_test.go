//go:build integration

package httpapi_test

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/JennGllg/mon-kado/internal/worker"
	"io"
	"log/slog"
	"net/url"
	"testing"
	"time"
)

func (f *fixture) administrator(t *testing.T, email string) string {
	t.Helper()
	id, _ := f.administratorWithAuthenticator(t, email)
	return id
}

func (f *fixture) administratorWithAuthenticator(t *testing.T, email string) (string, string) {
	t.Helper()
	f.access = ""
	f.csrfToken(t)
	id := f.member(t, email)
	if _, err := f.store.Pool.Exec(t.Context(), `INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE normalized_name='ADMINISTRATOR'`, id); err != nil {
		t.Fatal(err)
	}
	f.access = ""
	f.csrfToken(t)
	var challenge domain.TwoFactorChallengeResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", jsonInput(t, map[string]string{"email": email, "password": "A long test password 🎁"}), nil), 202, &challenge)
	var setup domain.TwoFactorSetupResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/setup", jsonInput(t, map[string]string{"flow": challenge.Flow}), nil), 200, &setup)
	code, err := security.AuthenticatorCode(setup.ManualKey, f.service.Now().Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/setup/confirmations", jsonInput(t, map[string]string{"flow": challenge.Flow, "code": code}), nil), 200, nil)
	var session domain.AccessTokenResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": challenge.Flow}), nil), 200, &session)
	f.access = session.AccessToken
	f.csrfToken(t)
	return id, setup.ManualKey
}

func TestModeration_WhenReportReviewedAndListSuspended_PreservesAuditAndPublicPrivacy(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner@example.test")
	ownerAccess := f.access
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Reported list","occasion":"other"}`, nil), 201, &list)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	u, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": u.Fragment}
	f.access = ""
	f.csrfToken(t)
	requireStatus(t, f.send(t, "POST", "/api/v1/shared-wishlists/"+link.Id+"/reports", `{"reason":"privacyViolation","details":"Private information"}`, headers), 204, nil)
	f.access = ownerAccess
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/reported-wishlists", "", nil), 403, nil)
	f.administrator(t, "administrator@example.test")
	var summaries domain.PaginatedResponse[application.ReportedWishlistSummary]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/reported-wishlists", "", nil), 200, &summaries)
	if summaries.TotalCount != 1 || len(summaries.Items) != 1 || summaries.Items[0].ReportCount != 1 {
		t.Fatal("report summary contract differs")
	}
	var reports domain.PaginatedResponse[domain.WishlistReportDetails]
	base := "/api/v1/admin/reported-wishlists/" + list.Id
	requireStatus(t, f.send(t, "GET", base, "", nil), 200, new(domain.ReportedWishlistResponse))
	requireStatus(t, f.send(t, "GET", base+"/reports", "", nil), 200, &reports)
	if len(reports.Items) != 1 {
		t.Fatal("report missing")
	}
	path := base + "/reports/" + reports.Items[0].Id
	read := f.send(t, "GET", path, "", nil)
	etag := read.Header.Get("ETag")
	requireStatus(t, read, 200, nil)
	// Act
	review := f.send(t, "PUT", path, `{"status":"upheld","reviewNote":"Reviewed by support"}`, map[string]string{"If-Match": etag})
	requireStatus(t, review, 200, nil)
	requireStatus(t, f.send(t, "PUT", path, `{"status":"dismissed"}`, map[string]string{"If-Match": etag}), 412, nil)
	var events domain.PaginatedResponse[domain.WishlistReportReviewEventDetails]
	requireStatus(t, f.send(t, "GET", path+"/events", "", nil), 200, &events)
	moderationPath := "/api/v1/admin/wishlists/" + list.Id + "/moderation"
	read = f.send(t, "GET", moderationPath, "", nil)
	etag = read.Header.Get("ETag")
	requireStatus(t, read, 200, nil)
	changed := f.send(t, "PUT", moderationPath, `{"isSuspended":true,"reason":"Privacy investigation"}`, map[string]string{"If-Match": etag})
	newETag := changed.Header.Get("ETag")
	var state domain.WishlistModerationResponse
	requireStatus(t, changed, 200, &state)
	requireStatus(t, f.send(t, "PUT", moderationPath, `{"isSuspended":true,"reason":"Privacy investigation"}`, map[string]string{"If-Match": newETag}), 200, nil)
	var history domain.PaginatedResponse[domain.WishlistModerationEventResponse]
	requireStatus(t, f.send(t, "GET", moderationPath+"/events", "", nil), 200, &history)
	// Assert
	if len(events.Items) != 1 || events.Items[0].Status != "upheld" || !state.IsSuspended || len(history.Items) != 1 {
		t.Fatal("moderation audit invariant violated")
	}
	f.access = ""
	requireStatus(t, f.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id, "", headers), 404, nil)
	f.access = ownerAccess
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Blocked gift"}`, nil), 409, nil)
}

func TestAdministration_WhenRevokingAndErasingMember_RetainsAuditAndExpiresEncryptedNotification(t *testing.T) {
	// Arrange
	f := newFixture(t)
	target := f.member(t, "target@example.test")
	oldAccess := f.access
	admin := f.administrator(t, "administrator@example.test")
	adminAccess := f.access
	input := jsonInput(t, map[string]string{"confirmedMemberId": target, "requestReference": "SUPPORT-42"})
	// Act
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+target+"/session-revocations", input, nil), 204, nil)
	f.access = oldAccess
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 401, nil)
	f.access = adminAccess
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+admin+"/erasure-requests", jsonInput(t, map[string]string{"confirmedMemberId": admin, "requestReference": "SUPPORT-SELF"}), nil), 409, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+target+"/erasure-requests", input, nil), 204, nil)
	var audit domain.PaginatedResponse[domain.AdministrativeAuditEventResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/audit-events?requestReference=SUPPORT-42", "", nil), 200, &audit)
	// Assert
	if audit.TotalCount != 2 || len(audit.Items) != 2 {
		t.Fatal("administrative actions were not audited")
	}
	var protected string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT protected_recipient FROM account_erasure_email_outbox`).Scan(&protected); err != nil {
		t.Fatal(err)
	}
	if protected == "target@example.test" {
		t.Fatal("erasure recipient persisted as plaintext")
	}
	now := f.service.Now().Add(25 * time.Hour)
	f.service.Now = func() time.Time { return now }
	w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	var count int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM account_erasure_email_outbox`).Scan(&count); err != nil {
		t.Fatal(err)
	}
	if count != 0 {
		t.Fatal("expired encrypted recipient was retained with email provider disabled")
	}
}
