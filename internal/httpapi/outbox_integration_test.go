//go:build integration

package httpapi_test

import (
	"context"
	"io"
	"log/slog"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/telemetry"
	"github.com/JennGllg/mon-kado/internal/worker"
)

type attemptedEmail struct {
	IDs      []string
	Messages []application.EmailMessage
	Failures []error
	Context  context.Context
}

func TestOutbox_WhenBackoffWouldOverflow_SaturatesAtConfiguredMaximum(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC().Truncate(time.Microsecond)
	f.service.Now = func() time.Time { return now }
	f.service.Config.MaximumAttempts = 30
	f.service.Config.JobOptions.FirstRetryDelay = 24 * time.Hour
	f.service.Config.JobOptions.MaximumRetryDelay = 72 * time.Hour
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", `{"email":"backoff@example.test","password":"A long test password 🎁","displayName":"Backoff member"}`, nil), 202, nil)
	if _, err := f.store.Pool.Exec(t.Context(), `UPDATE authentication_email_outbox SET attempt_count=20`); err != nil {
		t.Fatal(err)
	}
	sender := &attemptedEmail{Context: t.Context(), Failures: []error{&application.DeliveryError{Category: "network", Retryable: true}}}
	w := worker.Worker{Service: f.service, Sender: sender, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	// Act
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	// Assert
	var available time.Time
	var attempts int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT available_at,attempt_count FROM authentication_email_outbox`).Scan(&available, &attempts); err != nil {
		t.Fatal(err)
	}
	if attempts != 21 || !available.Equal(now.Add(72*time.Hour)) || len(sender.IDs) != 1 {
		t.Fatal("exponential backoff overflowed or replayed immediately")
	}
}

func (s *attemptedEmail) Send(ctx context.Context, id string, message application.EmailMessage) (string, error) {
	if ctx != s.Context {
		return "", context.Canceled
	}
	s.IDs = append(s.IDs, id)
	s.Messages = append(s.Messages, message)
	index := len(s.IDs) - 1
	if index < len(s.Failures) && s.Failures[index] != nil {
		return "", s.Failures[index]
	}
	return "accepted", nil
}

func TestOutbox_WhenTransientDeliveryFails_RetainsSameProofAndHonorsExplicitRetryAfter(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC().Truncate(time.Microsecond)
	f.service.Now = func() time.Time { return now }
	f.service.Config.MaximumAttempts = 3
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", `{"email":"outbox@example.test","password":"A long test password 🎁","displayName":"Outbox member"}`, nil), 202, nil)
	sender := &attemptedEmail{Context: t.Context(), Failures: []error{&application.DeliveryError{Category: "throttled", Retryable: true, RetryAfter: 90 * time.Second}, nil}}
	w := worker.Worker{Service: f.service, Sender: sender, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	// Act
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	var available time.Time
	var protected *string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT available_at,protected_payload FROM authentication_email_outbox`).Scan(&available, &protected); err != nil {
		t.Fatal(err)
	}
	now = now.Add(89 * time.Second)
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	if len(sender.IDs) != 1 {
		t.Fatal("delivery was retried before its explicit deadline")
	}
	now = now.Add(time.Second)
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	var processed *time.Time
	var retained *string
	var count int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT processed_at,protected_payload,attempt_count FROM authentication_email_outbox`).Scan(&processed, &retained, &count); err != nil {
		t.Fatal(err)
	}
	// Assert
	if protected == nil || retained != nil || processed == nil || count != 2 || len(sender.IDs) != 2 || sender.IDs[0] != sender.IDs[1] || sender.Messages[0] != sender.Messages[1] || sender.Messages[0].ActionURL == "" || !available.Equal(now) {
		t.Fatal("explicit retry lost its proof, identity, deadline or terminal secret cleanup")
	}
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	if len(sender.IDs) != 2 {
		t.Fatal("processed email was replayed")
	}
}

func TestOutbox_WhenDeliveryPermanentlyFails_ClearsProtectedPayloadAndNeverRetries(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.service.Config.MaximumAttempts = 3
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", `{"email":"terminal@example.test","password":"A long test password 🎁","displayName":"Terminal member"}`, nil), 202, nil)
	sender := &attemptedEmail{Context: t.Context(), Failures: []error{&application.DeliveryError{Category: "authorization", Retryable: false}}}
	w := worker.Worker{Service: f.service, Sender: sender, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	// Act
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	var protected *string
	var processed *time.Time
	var category string
	var attempts int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT protected_payload,processed_at,last_error,attempt_count FROM authentication_email_outbox`).Scan(&protected, &processed, &category, &attempts); err != nil {
		t.Fatal(err)
	}
	// Assert
	if len(sender.IDs) != 1 || protected != nil || processed == nil || category != "authorization" || attempts != 3 {
		t.Fatal("terminal delivery retained protected content or was retried")
	}
	if w.Telemetry.Capture().Jobs[telemetry.AuthenticationEmailDelivery].TerminalFailures != 1 {
		t.Fatal("terminal delivery was missing or counted again during later cleanup")
	}
}

func TestCleanup_WhenProviderDisabled_PurgesExpiredAuthenticationProofsInBoundedBatches(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	f.service.Config.MaximumAttempts = 3
	f.service.Config.JobOptions.BatchSize = 1
	for _, email := range []string{"first@example.test", "second@example.test"} {
		requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", jsonInput(t, map[string]string{"email": email, "password": "A long test password 🎁", "displayName": "Pending"}), nil), 202, nil)
	}
	rows, err := f.store.Pool.Query(t.Context(), `SELECT id::text FROM authentication_email_outbox ORDER BY id`)
	if err != nil {
		t.Fatal(err)
	}
	ids := []string{}
	for rows.Next() {
		var id string
		if err = rows.Scan(&id); err != nil {
			t.Fatal(err)
		}
		ids = append(ids, id)
	}
	rows.Close()
	for _, id := range ids {
		if _, err = f.service.PrepareAuthenticationEmail(t.Context(), id); err != nil {
			t.Fatal(err)
		}
	}
	now = now.Add(25 * time.Hour)
	w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	// Act
	if err = w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	var retained int
	if err = f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM authentication_email_outbox WHERE protected_payload IS NOT NULL`).Scan(&retained); err != nil {
		t.Fatal(err)
	}
	// Assert
	if retained != 1 {
		t.Fatal("cleanup did not honor its configured batch bound")
	}
	progress := w.Telemetry.Capture().Jobs
	if progress[telemetry.AuthenticationEmailDelivery].State != "disabled" || progress[telemetry.ProcessedAuthenticationEmailCleanup].Successes != 1 || progress[telemetry.AuthenticationEmailDelivery].TerminalFailures != 1 {
		t.Fatal("provider-independent cleanup or disabled delivery telemetry differs")
	}
	if err = w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err = f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM authentication_email_outbox WHERE protected_payload IS NOT NULL`).Scan(&retained); err != nil {
		t.Fatal(err)
	}
	if retained != 0 {
		t.Fatal("disabled provider prevented expired proof cleanup")
	}
}
