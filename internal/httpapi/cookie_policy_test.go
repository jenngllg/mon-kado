package httpapi_test

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

func TestCSRFCookies_WhenTransportAndEnvironmentDiffer_PreservesProductionSecurityAndLocalHTTP(t *testing.T) {
	for _, scenario := range []struct {
		name, origin, cookieName string
		production, secure       bool
	}{
		{"local HTTP", "http://127.0.0.1", "MonKado.Csrf", false, false},
		{"local HTTPS", "https://127.0.0.1", "MonKado.Csrf", false, true},
		{"production behind TLS proxy", "http://127.0.0.1", "__Host-MonKado.Csrf", true, true},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			cfg := config.Config{Production: scenario.production, AllowedHosts: []string{"127.0.0.1"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			request := httptest.NewRequest("GET", scenario.origin+"/security/csrf-token", nil)
			response := httptest.NewRecorder()
			// Act
			api.ServeHTTP(response, request)
			// Assert
			cookies := response.Result().Cookies()
			var token domain.CsrfTokenResponse
			if response.Code != 200 || json.Unmarshal(response.Body.Bytes(), &token) != nil || token.Token == "" || len(cookies) != 1 {
				t.Fatal("CSRF response contract differs")
			}
			cookie := cookies[0]
			if cookie.Name != scenario.cookieName || cookie.Secure != scenario.secure || !cookie.HttpOnly || cookie.SameSite != http.SameSiteStrictMode || cookie.Path != "/" || cookie.Domain != "" {
				t.Fatal("cookie policy differs for the public transport and environment")
			}
		})
	}
}

func TestCSRFToken_WhenRefreshCookieDiffers_ReportsOnlyPresence(t *testing.T) {
	for _, scenario := range []struct {
		name, cookieName, cookieValue string
		production, present           bool
	}{
		{"anonymous", "", "", false, false},
		{"empty local cookie", "MonKado.Refresh", "", false, false},
		{"unvalidated local cookie", "MonKado.Refresh", "not-an-authentication-proof", false, true},
		{"unrelated cookie", "Other.Refresh", "not-a-session", false, false},
		{"production ignores local cookie", "MonKado.Refresh", "not-a-session", true, false},
		{"production cookie", "__Host-MonKado.Refresh", "not-an-authentication-proof", true, true},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange: no database or session authentication is needed to report cookie presence.
			cfg := config.Config{Production: scenario.production, AllowedHosts: []string{"127.0.0.1"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			request := httptest.NewRequest("GET", "https://127.0.0.1/security/csrf-token", nil).WithContext(t.Context())
			if scenario.cookieName != "" {
				request.AddCookie(&http.Cookie{Name: scenario.cookieName, Value: scenario.cookieValue})
			}
			response := httptest.NewRecorder()
			// Act
			api.ServeHTTP(response, request)
			// Assert
			var result domain.CsrfTokenResponse
			if response.Code != 200 || json.Unmarshal(response.Body.Bytes(), &result) != nil || result.Token == "" || result.HasSessionCookie != scenario.present {
				t.Fatal("cookie presence differs")
			}
			var fields map[string]json.RawMessage
			if json.Unmarshal(response.Body.Bytes(), &fields) != nil || len(fields) != 2 || fields["hasSessionCookie"] == nil {
				t.Fatal("unexpected session data in CSRF response")
			}
		})
	}
}
