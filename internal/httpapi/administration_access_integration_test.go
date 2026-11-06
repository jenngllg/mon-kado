//go:build integration

package httpapi_test

import (
	"io"
	"log/slog"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestAdministration_WhenCallerLacksCurrentAdministratorMFA_RejectsWithoutEffects(t *testing.T) {
	for _, scenario := range []struct {
		name   string
		status int
	}{
		{"anonymous", 401},
		{"member", 403},
		{"administratorWithoutMFA", 403},
		{"revokedRole", 403},
		{"replacedAuthenticator", 401},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			target := f.member(t, "target@example.test")
			targetAccess := f.access
			var list domain.WishlistResponse
			requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Preserved list","occasion":"other"}`, nil), 201, &list)
			var wish domain.WishResponse
			requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Preserved wish"}`, nil), 201, &wish)
			var caller string
			if scenario.name == "revokedRole" || scenario.name == "replacedAuthenticator" {
				caller = f.administrator(t, "caller@example.test")
			} else {
				caller = f.member(t, "caller@example.test")
			}
			switch scenario.name {
			case "anonymous":
				f.access = ""
			case "administratorWithoutMFA":
				if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET is_admin=true WHERE id=$1`, caller); err != nil {
					t.Fatal(err)
				}
			case "revokedRole":
				if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET is_admin=false WHERE id=$1`, caller); err != nil {
					t.Fatal(err)
				}
			case "replacedAuthenticator":
				if _, err := f.store.Pool.Exec(t.Context(), `UPDATE member_two_factors SET credential_id=$2 WHERE member_id=$1`, caller, security.NewID()); err != nil {
					t.Fatal(err)
				}
			}
			input := jsonInput(t, map[string]string{"confirmedMemberId": target, "requestReference": "ACCESS-REJECTION"})

			// Act
			for _, action := range []string{"erasure-requests", "session-revocations"} {
				requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+target+"/"+action, input, nil), scenario.status, nil)
			}
			requireStatus(t, f.send(t, "GET", "/api/v1/admin/audit-events", "", nil), scenario.status, nil)
			requireStatus(t, f.send(t, "GET", "/api/v1/admin/members", "", nil), scenario.status, nil)
			requireStatus(t, f.send(t, "GET", "/api/v1/admin/members/"+target, "", nil), scenario.status, nil)
			// Exercise every registered administrator operation, including future routes,
			// before input validation, so malformed payloads cannot mask missing guards.
			api := httpapi.New(f.service, slog.New(slog.NewJSONHandler(io.Discard, nil)))
			replace := strings.NewReplacer("{memberId}", target, "{wishlistId}", list.Id, "{wishId}", wish.Id, "{reportId}", security.NewID(), "{exportId}", security.NewID(), "{impersonationId}", security.NewID())
			for _, pattern := range api.Patterns() {
				method, path, _ := strings.Cut(pattern, " ")
				if !strings.HasPrefix(path, "/api/v1/admin/") {
					continue
				}
				t.Run(pattern, func(t *testing.T) {
					requireStatus(t, f.send(t, method, replace.Replace(path), input, nil), scenario.status, nil)
				})
			}

			// Assert: the target session and owned data remain usable through public contracts.
			f.access = targetAccess
			requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, nil)
			requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+list.Id, "", nil), 200, nil)
			requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+list.Id+"/wishes/"+wish.Id, "", nil), 200, nil)
			var effects int
			if err := f.store.Pool.QueryRow(t.Context(), `SELECT (SELECT count(*) FROM administrative_account_erasure_events)+(SELECT count(*) FROM administrative_session_revocation_events)+(SELECT count(*) FROM account_erasure_email_outbox)`).Scan(&effects); err != nil {
				t.Fatal(err)
			}
			if effects != 0 {
				t.Fatalf("rejected actions produced %d administrative side effects", effects)
			}
		})
	}
}
