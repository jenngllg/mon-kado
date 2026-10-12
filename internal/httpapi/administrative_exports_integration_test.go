//go:build integration

package httpapi_test

import (
	"io"
	"log/slog"
	"net/url"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func TestAdministrativeExports_WhenRequestedAndDownloaded_AuditsBothActionsAndChecksLiveRole(t *testing.T) {
	// Arrange
	f := newFixture(t)
	target := f.member(t, "exporttarget@example.test")
	admin := f.administrator(t, "support@example.test")
	now := time.Now().UTC().Truncate(time.Microsecond)
	f.service.Now = func() time.Time { return now }
	f.service.Crypto.Now = f.service.Now
	base := "/api/v1/admin/members/" + target + "/data-exports"
	var result domain.PersonalDataExportResponse
	// Act
	requireStatus(t, f.send(t, "POST", base, `{"requestReference":"SUPPORT-EXPORT"}`, nil), 202, &result)
	requireStatus(t, f.send(t, "GET", base+"/latest", "", nil), 200, &result)
	w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", base+"/"+result.Id+"/archive", "", nil), 200, nil)
	var reused domain.PersonalDataExportResponse
	requireStatus(t, f.send(t, "POST", base, `{"requestReference":"SUPPORT-REUSE"}`, nil), 200, &reused)
	// Assert
	if reused.Id != result.Id || reused.Status != "ready" {
		t.Fatal("administrative request failed to reuse ready archive")
	}
	query := url.Values{"memberId": {target}, "administratorId": {admin}, "exportId": {result.Id}, "requestReference": {"SUPPORT-EXPORT"}}
	var audit domain.PaginatedResponse[domain.AdministrativeAuditEventResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/audit-events?"+query.Encode(), "", nil), 200, &audit)
	if audit.TotalCount != 2 || len(audit.Items) != 2 {
		t.Fatal("administrative download and request were not audited together")
	}
	query.Set("action", "MEMBERDATAEXPORTDOWNLOADSTARTED")
	query.Set("to", now.Add(time.Nanosecond).Format(time.RFC3339Nano))
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/audit-events?"+query.Encode(), "", nil), 200, &audit)
	if audit.TotalCount != 1 || audit.Items[0].Action != "memberDataExportDownloadStarted" {
		t.Fatal("half-open upper boundary or action filter differs")
	}
	query.Del("to")
	query.Set("from", now.Add(time.Nanosecond).Format(time.RFC3339Nano))
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/audit-events?"+query.Encode(), "", nil), 200, &audit)
	if audit.TotalCount != 0 || len(audit.Items) != 0 {
		t.Fatal("half-open lower boundary included an earlier event")
	}
	query.Del("from")
	query.Set("requestReference", "support-export")
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/audit-events?"+query.Encode(), "", nil), 200, &audit)
	if audit.TotalCount != 0 {
		t.Fatal("support reference matching was not case sensitive")
	}
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+security.NewID()+"/data-exports", `{"requestReference":"SUPPORT-MISSING"}`, nil), 404, nil)
	principal, err := f.service.Authenticate(t.Context(), f.access)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = f.store.Pool.Exec(t.Context(), `DELETE FROM user_roles WHERE user_id=$1 AND role_id=(SELECT id FROM roles WHERE normalized_name='ADMINISTRATOR')`, admin); err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", base+"/"+result.Id+"/archive", "", nil), 403, nil)
	file, _, err := f.service.ExportArchive(t.Context(), target, result.Id, principal, true)
	if file != nil {
		file.Close()
		t.Fatal("archive returned after role revocation")
	}
	if err == nil {
		t.Fatal("stale administrator principal bypassed transactional authorization")
	}
}
