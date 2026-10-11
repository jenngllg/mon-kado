//go:build integration

package httpapi_test

import (
	"io"
	"log/slog"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/telemetry"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func TestExports_WhenGenerationFails_RecordsBoundedAttemptsAndStableFailureCode(t *testing.T) {
	for _, test := range []struct {
		name, code string
		sizeLimit  bool
		attempts   int
	}{
		{name: "size limit", sizeLimit: true, attempts: 1, code: domain.MemberDataExportTooLarge},
		{name: "unavailable storage", attempts: 2, code: domain.MemberDataExportGenerationFailed},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			f.member(t, "failedexport@example.test")
			now := time.Now().UTC()
			f.service.Now = func() time.Time { return now }
			f.service.Crypto.Now = f.service.Now
			f.service.Config.JobOptions.ExportMaximumAttempts = 2
			if test.sizeLimit {
				f.service.Config.JobOptions.ExportMaximumBytes = 1024
			} else {
				path := filepath.Join(t.TempDir(), "unavailable")
				if err := os.WriteFile(path, []byte("occupied"), 0600); err != nil {
					t.Fatal(err)
				}
				f.service.Config.ExportPath = path
			}
			var result domain.PersonalDataExportResponse
			requireStatus(t, f.send(t, "POST", "/api/v1/members/current/data-exports", "", nil), 202, &result)
			w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
			t.Cleanup(w.Close)
			// Act
			for attempt := 0; attempt < test.attempts; attempt++ {
				// The reconciliation job also detects the intentionally unavailable directory.
				if err := w.Tick(t.Context()); err != nil && test.sizeLimit {
					t.Fatal(err)
				}
				now = now.Add(time.Minute)
			}
			requireStatus(t, f.send(t, "GET", "/api/v1/members/current/data-exports/"+result.Id, "", nil), 200, &result)
			// Assert
			if result.Status != "failed" || result.ErrorCode == nil || *result.ErrorCode != test.code || result.SizeInBytes != nil || result.ReadyAt != nil {
				t.Fatalf("unexpected terminal export: %#v", result)
			}
			var attempts, notifications int
			if err := f.store.Pool.QueryRow(t.Context(), `SELECT attempt_count,(SELECT count(*) FROM authentication_email_outbox WHERE member_data_export_id=$1) FROM member_data_exports WHERE id=$1`, result.Id).Scan(&attempts, &notifications); err != nil {
				t.Fatal(err)
			}
			if attempts != test.attempts || notifications != 0 {
				t.Fatal("failed export attempts or publication notification are incorrect")
			}
			if w.Telemetry.Capture().Jobs[telemetry.PersonalDataExport].TerminalFailures != 1 {
				t.Fatal("terminal export telemetry differs from its persisted outcome")
			}
			requireStatus(t, f.send(t, "GET", "/api/v1/members/current/data-exports/"+result.Id+"/archive", "", nil), 409, nil)
		})
	}
}

func TestExports_WhenRequestsRepeat_ReusesActiveWorkAndEnforcesDailyLimit(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "quotaexport@example.test")
	f.service.Config.JobOptions.ExportMaximumBytes = 1024
	w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	var previous string
	// Act
	for index := 0; index < 3; index++ {
		var requested, repeated domain.PersonalDataExportResponse
		requireStatus(t, f.send(t, "POST", "/api/v1/members/current/data-exports", "", nil), 202, &requested)
		requireStatus(t, f.send(t, "POST", "/api/v1/members/current/data-exports", "", nil), 202, &repeated)
		if requested.Id != repeated.Id || requested.Id == previous {
			t.Fatal("active request reuse contract differs")
		}
		if err := w.Tick(t.Context()); err != nil {
			t.Fatal(err)
		}
		previous = requested.Id
	}
	// Assert
	var failure domain.ErrorResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/members/current/data-exports", "", nil), 429, &failure)
	if failure.ErrorCode == nil || *failure.ErrorCode != domain.MemberDataExportRateLimited {
		t.Fatal("daily export limit code differs")
	}
}
