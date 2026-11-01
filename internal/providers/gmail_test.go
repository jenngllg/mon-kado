package providers

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"io"
	"net/http"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
)

type transportFunc func(*http.Request) (*http.Response, error)

func (f transportFunc) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }
func providerResponse(status int, body string) *http.Response {
	return &http.Response{StatusCode: status, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(body))}
}

func TestSend_WhenGmailSucceeds_UsesSinglePOSTAndPropagatesContext(t *testing.T) {
	// Arrange
	now := time.Date(2026, 10, 11, 12, 0, 0, 0, time.UTC)
	cfg := config.Config{GmailSender: "sender@example.test", GmailClientID: "client", GmailClientSecret: "secret", GmailRefreshToken: "refresh", HTTPTimeout: time.Second}
	gmail, err := NewGmail(cfg, func() time.Time { return now })
	if err != nil {
		t.Fatal(err)
	}
	ctx := context.WithValue(t.Context(), struct{}{}, "proof")
	calls := []string{}
	gmail.HTTP.Transport = transportFunc(func(r *http.Request) (*http.Response, error) {
		if r.Context().Value(struct{}{}) != "proof" {
			t.Fatal("context was substituted")
		}
		calls = append(calls, r.URL.Path)
		if r.Method != "POST" {
			t.Fatal("provider method differs")
		}
		if r.URL.Path == "/token" {
			if err := r.ParseForm(); err != nil || r.Form.Get("refresh_token") != "refresh" {
				t.Fatal("OAuth refresh payload differs")
			}
			return providerResponse(200, `{"access_token":"access","expires_in":3600}`), nil
		}
		if r.Header.Get("Authorization") != "Bearer access" {
			t.Fatal("Gmail authorization differs")
		}
		var payload struct {
			Raw string `json:"raw"`
		}
		if err := json.NewDecoder(r.Body).Decode(&payload); err != nil {
			t.Fatal(err)
		}
		mime, err := base64.RawURLEncoding.DecodeString(payload.Raw)
		if err != nil || !strings.Contains(string(mime), "Message-ID: <01900000000070008000000000000001@mon-kado.fr>") {
			t.Fatal("MIME message identity differs")
		}
		return providerResponse(200, `{"id":"gmail-id"}`), nil
	})
	// Act
	id, err := gmail.Send(ctx, "01900000-0000-7000-8000-000000000001", application.EmailMessage{To: "member@example.test", Subject: "Bonjour 🎁", Text: "Confirmation"})
	// Assert
	if err != nil || id != "gmail-id" || len(calls) != 2 {
		t.Fatalf("unexpected delivery: id=%s, calls=%v, err=%v", id, calls, err)
	}
}

func TestSend_WhenPOSTFails_DoesNotReplay(t *testing.T) {
	for _, status := range []int{400, 401, 403, 408, 429, 500, 503} {
		// Arrange
		now := time.Now()
		gmail := &GmailClient{HTTP: &http.Client{}, Config: config.Config{GmailSender: "sender@example.test"}, Now: func() time.Time { return now }, SendURL: "https://gmail.example.test/messages/send", token: "access", expires: now.Add(time.Hour)}
		calls := 0
		gmail.HTTP.Transport = transportFunc(func(r *http.Request) (*http.Response, error) {
			calls++
			return providerResponse(status, `{"error":"sensitive provider body"}`), nil
		})
		// Act
		_, err := gmail.Send(t.Context(), "01900000-0000-7000-8000-000000000001", application.EmailMessage{To: "member@example.test", Subject: "Subject", Text: "Message"})
		// Assert
		if err == nil || calls != 1 || strings.Contains(err.Error(), "sensitive") {
			t.Fatalf("status %d was replayed or leaked its response", status)
		}
	}
}
