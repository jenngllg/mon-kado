//go:build integration

package httpapi_test

import (
	"context"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/JennGllg/mon-kado/internal/telemetry"
	"github.com/JennGllg/mon-kado/internal/worker"
	"log/slog"
	"sync"
	"testing"
	"time"
)

type blockedEmail struct {
	Started chan struct{}
	once    sync.Once
}

func (s *blockedEmail) Send(ctx context.Context, id string, message application.EmailMessage) (string, error) {
	s.once.Do(func() { close(s.Started) })
	<-ctx.Done()
	return "", ctx.Err()
}

type jobSignal struct {
	Done chan struct{}
	once *sync.Once
}

func (h jobSignal) Enabled(context.Context, slog.Level) bool { return true }
func (h jobSignal) Handle(ctx context.Context, record slog.Record) error {
	if record.Message != "Worker pass completed" {
		return nil
	}
	record.Attrs(func(attribute slog.Attr) bool {
		if attribute.Key == "Job" && attribute.Value.String() == "erasureExpiry" {
			h.once.Do(func() { close(h.Done) })
		}
		return true
	})
	return nil
}
func (h jobSignal) WithAttrs([]slog.Attr) slog.Handler { return h }
func (h jobSignal) WithGroup(string) slog.Handler      { return h }

func TestWorker_WhenEmailSendBlocked_StillPurgesExpiredErasureAndStopsOnCancellation(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.service.Config.MaximumAttempts = 3
	f.service.Config.WorkerInterval = time.Hour
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", `{"email":"pending@example.test","password":"A long test password 🎁","displayName":"Pending member"}`, nil), 202, nil)
	id := security.NewID()
	now := f.service.Now().UTC()
	protected, err := f.service.Crypto.Protect("erased@example.test", "erasure-recipient|"+id)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = f.store.Pool.Exec(t.Context(), `INSERT INTO administrative_account_erasure_events(id,member_id,request_reference,created_at,notification_status) VALUES($1,$2,'SUPPORT-EXPIRY',$3,'Pending')`, id, security.NewID(), now.Add(-25*time.Hour)); err != nil {
		t.Fatal(err)
	}
	if _, err = f.store.Pool.Exec(t.Context(), `INSERT INTO account_erasure_email_outbox(id,protected_recipient,created_at,expires_at,available_at,attempt_count) VALUES($1,$2,$3,$4,$3,0)`, id, protected, now.Add(-25*time.Hour), now.Add(-time.Hour)); err != nil {
		t.Fatal(err)
	}
	sender := &blockedEmail{Started: make(chan struct{})}
	signal := jobSignal{Done: make(chan struct{}), once: &sync.Once{}}
	w := worker.Worker{Service: f.service, Sender: sender, Logger: slog.New(signal)}
	ctx, cancel := context.WithCancel(t.Context())
	t.Cleanup(cancel)
	finished := make(chan error, 1)
	// Act
	go func() { finished <- w.Run(ctx) }()
	select {
	case <-sender.Started:
	case <-t.Context().Done():
		t.Fatal("worker did not start email attempt")
	}
	select {
	case <-signal.Done:
	case <-t.Context().Done():
		t.Fatal("erasure expiry was blocked by email send")
	}
	var count int
	if err = f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM account_erasure_email_outbox WHERE id=$1`, id).Scan(&count); err != nil {
		t.Fatal(err)
	}
	cancel()
	err = <-finished
	// Assert
	if count != 0 || err != nil {
		t.Fatal("independent cleanup or cooperative worker cancellation failed")
	}
	if w.Telemetry.Capture().Jobs[telemetry.AccountErasureProcessing].TerminalFailures != 1 {
		t.Fatal("expired recipient outcome was not recorded")
	}
}
