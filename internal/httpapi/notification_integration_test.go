//go:build integration

package httpapi_test

import (
	"io"
	"log/slog"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func TestErasureNotification_WhenProviderCompletesOrFails_RecordsOutcomeAndPurgesRecipient(t *testing.T) {
	for _, tc := range []struct {
		name     string
		failures []error
		corrupt  bool
		accepted bool
		attempts int
	}{
		{"accepted", nil, false, true, 1},
		{"transientThenAccepted", []error{&application.DeliveryError{Category: "network", Retryable: true}}, false, true, 2},
		{"permanent", []error{&application.DeliveryError{Category: "authorization", Retryable: false}}, false, false, 1},
		{"attemptLimit", []error{&application.DeliveryError{Category: "network", Retryable: true}, &application.DeliveryError{Category: "network", Retryable: true}}, false, false, 2},
		{"corruptRecipient", nil, true, false, 0},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			now := time.Now().UTC().Truncate(time.Microsecond)
			f.service.Now = func() time.Time { return now }
			f.service.Config.MaximumAttempts = 2
			target := f.member(t, "erasure-target@example.test")
			f.administrator(t, "notification-admin@example.test")
			sender := &attemptedEmail{Context: t.Context()}
			w := worker.Worker{Service: f.service, Sender: sender, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
			t.Cleanup(w.Close)
			if err := w.Tick(t.Context()); err != nil {
				t.Fatal(err)
			}
			sender.IDs, sender.Messages = nil, nil
			sender.Failures = tc.failures
			requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+target+"/erasure-requests", jsonInput(t, map[string]string{"confirmedMemberId": target, "requestReference": "SUPPORT-NOTIFICATION"}), nil), 204, nil)
			if tc.corrupt {
				if _, err := f.store.Pool.Exec(t.Context(), `UPDATE account_erasure_email_outbox SET protected_recipient='invalid-ciphertext'`); err != nil {
					t.Fatal(err)
				}
			}
			// Act
			if err := w.Tick(t.Context()); err != nil {
				t.Fatal(err)
			}
			var pending int
			if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM account_erasure_email_outbox`).Scan(&pending); err != nil {
				t.Fatal(err)
			}
			if tc.name == "transientThenAccepted" || tc.name == "attemptLimit" || tc.corrupt {
				if pending != 1 {
					t.Fatal("retryable notification lost its encrypted recipient")
				}
				if err := w.Tick(t.Context()); err != nil {
					t.Fatal(err)
				}
				now = now.Add(time.Minute)
				if err := w.Tick(t.Context()); err != nil {
					t.Fatal(err)
				}
			}
			// Assert
			var status string
			if err := f.store.Pool.QueryRow(t.Context(), `SELECT notification_status FROM administrative_account_erasure_events WHERE member_id=$1`, target).Scan(&status); err != nil {
				t.Fatal(err)
			}
			expected := "Failed"
			if tc.accepted {
				expected = "Accepted"
			}
			if status != expected || len(sender.IDs) != tc.attempts {
				t.Fatal("notification outcome or provider attempt count differs")
			}
			if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM account_erasure_email_outbox`).Scan(&pending); err != nil || pending != 0 {
				t.Fatal("terminal notification retained its encrypted recipient")
			}
			for _, message := range sender.Messages {
				if message.To != "erasure-target@example.test" || message.Subject != "Votre compte a été supprimé – MonKado" || message.ActionURL != "" {
					t.Fatal("erasure notification contract differs")
				}
			}
			if len(sender.IDs) == 2 && sender.IDs[0] != sender.IDs[1] {
				t.Fatal("retry changed notification identity")
			}
			if err := w.Tick(t.Context()); err != nil {
				t.Fatal(err)
			}
			if len(sender.IDs) != tc.attempts {
				t.Fatal("terminal notification was replayed")
			}
		})
	}
}

func TestModerationNotification_WhenStateChanges_DeliversEachDecisionOnce(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "moderation-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Private wishlist contents","occasion":"other"}`, nil), 201, &list)
	f.administrator(t, "moderation-admin@example.test")
	sender := &attemptedEmail{Context: t.Context()}
	w := worker.Worker{Service: f.service, Sender: sender, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	sender.IDs, sender.Messages = nil, nil
	path := "/api/v1/admin/wishlists/" + list.Id + "/moderation"
	// Act
	for _, input := range []string{`{"isSuspended":true,"reason":"Initial reason"}`, `{"isSuspended":true,"reason":"Updated reason"}`, `{"isSuspended":false}`} {
		read := f.send(t, "GET", path, "", nil)
		etag := read.Header.Get("ETag")
		requireStatus(t, read, 200, nil)
		requireStatus(t, f.send(t, "PUT", path, input, map[string]string{"If-Match": etag}), 200, nil)
	}
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	// Assert
	if len(sender.Messages) != 3 {
		t.Fatal("moderation notification was missing or replayed")
	}
	for i, decision := range []string{"Votre liste a été suspendue", "Le motif de suspension de votre liste a été modifié", "Votre liste a été réactivée"} {
		message := sender.Messages[i]
		if message.To != "moderation-owner@example.test" || message.Subject != decision+" – MonKado" || !strings.Contains(message.Text, f.service.Config.FrontendOrigin) || strings.Contains(message.Text, list.Name) {
			t.Fatal("moderation email decision or privacy differs")
		}
	}
	if !strings.Contains(sender.Messages[0].Text, "Initial reason") || !strings.Contains(sender.Messages[1].Text, "Updated reason") || strings.Contains(sender.Messages[2].Text, "Motif :") {
		t.Fatal("moderation reason was not matched to its historical event")
	}
}
