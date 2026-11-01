package httpapi_test

import (
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

type failedBody struct{}

func (failedBody) Read([]byte) (int, error) { return 0, errors.New("private-reader-error") }

func TestServeHTTP_WhenRequestBodyIsUnusable_ReturnsExactErrorContractBeforeBusinessIO(t *testing.T) {
	for _, tc := range []struct {
		name, media, content string
		status               int
		broken               bool
	}{
		{"unsupported", "text/plain", `{}`, 415, false},
		{"malformedMedia", "application/json; charset", `{}`, 415, false},
		{"oversized", "application/json", strings.Repeat("x", 4097), 413, false},
		{"invalidUnicode", "application/json", `{"displayName":"\uD800"}`, 400, false},
		{"invalidJSON", "application/json", `{`, 400, false},
		{"multipleObjects", "application/json", `{} {}`, 400, false},
		{"readFailure", "application/json", "", 400, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange: no persistence dependency exists; binding must reject first.
			cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			csrf := httptest.NewRecorder()
			api.ServeHTTP(csrf, httptest.NewRequest("GET", "http://example.test/security/csrf-token", nil).WithContext(t.Context()))
			var token struct{ Token string }
			if err := json.Unmarshal(csrf.Body.Bytes(), &token); err != nil {
				t.Fatal(err)
			}
			var body io.Reader = strings.NewReader(tc.content)
			if tc.broken {
				body = failedBody{}
			}
			r := httptest.NewRequest("POST", "http://example.test/api/v1/auth/registrations", body).WithContext(t.Context())
			r.Header.Set("Content-Type", tc.media)
			r.Header.Set("X-CSRF-TOKEN", token.Token)
			for _, cookie := range csrf.Result().Cookies() {
				r.AddCookie(cookie)
			}
			w := httptest.NewRecorder()
			// Act
			api.ServeHTTP(w, r)
			// Assert
			var envelope map[string]any
			if w.Code != tc.status || json.Unmarshal(w.Body.Bytes(), &envelope) != nil {
				t.Fatalf("boundary status = %d", w.Code)
			}
			if len(envelope) != 5 {
				t.Fatal("error response schema differs")
			}
			for _, key := range []string{"statusCode", "title", "message", "errorCode", "validationErrors"} {
				if _, exists := envelope[key]; !exists {
					t.Fatalf("missing %s", key)
				}
			}
			if strings.Contains(w.Body.String(), "private-reader-error") || w.Header().Get("Cache-Control") != "no-store" {
				t.Fatal("body failure disclosed details or lost cache protection")
			}
		})
	}
}

func TestServeHTTP_WhenValidW3CTraceIsProvided_PropagatesTraceOnPublicResponse(t *testing.T) {
	// Arrange
	cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
	api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
	r := httptest.NewRequest(http.MethodGet, "http://example.test/liveness", nil).WithContext(t.Context())
	trace := "0123456789abcdef0123456789abcdef"
	r.Header.Set("traceparent", "00-"+trace+"-0123456789abcdef-01")
	w := httptest.NewRecorder()
	// Act
	api.ServeHTTP(w, r)
	// Assert
	if w.Code != 200 || w.Header().Get("X-Trace-ID") != trace {
		t.Fatal("valid parent trace was replaced")
	}
}

func TestServeHTTP_WhenSharedAggregateQuotaIsReached_RejectsBeforeResolvingResource(t *testing.T) {
	// Arrange
	cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
	now := time.Date(2026, 10, 12, 0, 0, 0, 0, time.UTC)
	api := httpapi.New(application.New(nil, cfg, func() time.Time { return now }), slog.New(slog.NewJSONHandler(io.Discard, nil)))
	request := func() *httptest.ResponseRecorder {
		r := httptest.NewRequest("GET", "http://example.test/api/v1/shared-wishlists/unknown/unknown", nil).WithContext(t.Context())
		w := httptest.NewRecorder()
		api.ServeHTTP(w, r)
		return w
	}
	for i := 0; i < 60; i++ {
		if request().Code != 404 {
			t.Fatal("quota refused request prematurely")
		}
	}
	// Act
	rejected := request()
	// Assert
	if rejected.Code != 429 || rejected.Header().Get("Retry-After") != "60" || rejected.Header().Get("Cache-Control") != "no-store" {
		t.Fatal("aggregate quota contract differs")
	}
	now = now.Add(time.Minute)
	if request().Code != 404 {
		t.Fatal("shared quota did not replenish")
	}
}
