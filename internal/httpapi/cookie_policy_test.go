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
