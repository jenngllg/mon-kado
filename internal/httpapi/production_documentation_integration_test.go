//go:build integration

package httpapi_test

import (
	"io"
	"log/slog"
	"net/http/httptest"
	"testing"

	"github.com/JennGllg/mon-kado/internal/httpapi"
)

func TestProductionDocumentation_WhenAnonymousMemberOrAdministrator_NeverServesSpecification(t *testing.T) {
	// Arrange: use real member and MFA-verified administrator sessions.
	f := newFixture(t)
	f.member(t, "documentation-member@example.test")
	member := f.access
	f.administrator(t, "documentation-admin@example.test")
	administrator := f.access
	f.service.Config.Production = true
	production := httptest.NewServer(httpapi.New(f.service, slog.New(slog.NewJSONHandler(io.Discard, nil))))
	t.Cleanup(production.Close)
	f.server = production

	// Act / Assert: the route is absent for every identity, while authorized API access works.
	for _, identity := range []struct{ name, token string }{{"anonymous", ""}, {"member", member}, {"administrator", administrator}} {
		t.Run(identity.name, func(t *testing.T) {
			f.access = identity.token
			for _, method := range []string{"GET", "HEAD"} {
				requireStatus(t, f.send(t, method, "/openapi/v1.json", "", nil), 404, nil)
			}
			if identity.token != "" {
				requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, nil)
			}
		})
	}
	f.access = administrator
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members", "", nil), 200, nil)
}
