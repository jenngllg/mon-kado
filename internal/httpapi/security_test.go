package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"io"
	"log/slog"
	"net/http/httptest"
	"net/netip"
	"testing"
	"time"
)

func TestServeHTTP_WhenBoundaryInputsDiffer_PreservesSecurityHeadersAndErrors(t *testing.T) {
	for _, test := range []struct {
		Name, Path, Host, Origin, Method string
		Status                           int
	}{{"liveness", "/liveness", "example.com", "", "GET", 200}, {"unknown", "/unknown", "example.com", "", "GET", 404}, {"badHost", "/liveness", "attacker.test", "", "GET", 400}, {"badOrigin", "/liveness", "example.com", "https://attacker.test", "GET", 403}, {"preflight", "/api/v1/wishlists", "example.com", "https://frontend.test", "OPTIONS", 204}, {"unauthenticated", "/api/v1/wishlists", "example.com", "", "GET", 401}, {"disabledGoogle", "/api/v1/auth/google", "example.com", "", "GET", 503}} {
		t.Run(test.Name, func(t *testing.T) {
			// Arrange
			cfg := config.Config{AllowedHosts: []string{"example.com"}, AllowedOrigins: []string{"https://frontend.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
			api := New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			r := httptest.NewRequest(test.Method, "http://"+test.Host+test.Path, nil)
			r.Header.Set("Origin", test.Origin)
			r.Header.Set("X-Correlation-ID", "019a0983-3456-7000-8000-000000000001")
			w := httptest.NewRecorder()
			// Act
			api.ServeHTTP(w, r)
			// Assert
			if w.Code != test.Status || w.Header().Get("Cache-Control") != "no-store" || w.Header().Get("X-Correlation-ID") != "019a0983-3456-7000-8000-000000000001" || len(w.Header().Get("X-Trace-ID")) != 32 {
				t.Fatalf("boundary contract differs: %d %s", w.Code, w.Body.String())
			}
		})
	}
}

func TestIncomingTrace_WhenW3CParentInvalid_DoesNotAcceptPartialHex(t *testing.T) {
	// Arrange
	trace := "0123456789abcdef0123456789abcdef"
	for _, value := range []string{"00-" + trace + "-0123456789abcdef-01", "00-0123456789abcdefzzzzzzzzzzzzzzzz-0123456789abcdef-01", "ff-" + trace + "-0123456789abcdef-01", "00-" + trace + "-0000000000000000-01", "00-00000000000000000000000000000000-0123456789abcdef-01", "00-" + trace + "-0123456789abcdef-zz"} {
		// Act
		result := incomingTrace(value)
		// Assert
		if (result != "") != (value == "00-"+trace+"-0123456789abcdef-01") {
			t.Fatal("invalid trace parent accepted")
		}
	}
}

func TestClientAddress_WhenPeerIsUntrusted_IgnoresForgedForwardedAddresses(t *testing.T) {
	// Arrange
	a := &API{Service: &application.Service{Config: config.Config{TrustedProxies: []netip.Prefix{netip.MustParsePrefix("127.0.0.1/32")}}}}
	r := httptest.NewRequest("GET", "http://example.com/liveness", nil)
	r.Header.Set("X-Forwarded-For", "8.8.8.8")
	r.RemoteAddr = "9.9.9.9:1234"
	// Act
	untrusted := a.clientAddress(r)
	r.RemoteAddr = "127.0.0.1:1234"
	trusted := a.clientAddress(r)
	// Assert
	if untrusted != "9.9.9.9" || trusted != "8.8.8.8" {
		t.Fatal("proxy trust boundary failed")
	}
}
