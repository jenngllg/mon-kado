package httpapi_test

import (
	"io"
	"log/slog"
	"net/http/httptest"
	"net/netip"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

func TestPerimeter_WhenQuotaIsExhausted_RejectsBeforeBearerAndIgnoresUntrustedForwarding(t *testing.T) {
	// Arrange
	for _, trusted := range []bool{false, true} {
		t.Run(map[bool]string{false: "untrusted", true: "trusted"}[trusted], func(t *testing.T) {
			cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32), PerimeterOptions: config.PerimeterOptions{PermitLimit: 1, Window: time.Minute}}
			if trusted {
				cfg.TrustedProxies = []netip.Prefix{netip.MustParsePrefix("192.0.2.1/32")}
			}
			now := time.Date(2026, 10, 11, 12, 0, 0, 0, time.UTC)
			api := httpapi.New(application.New(nil, cfg, func() time.Time { return now }), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			request := func(path, forwarded, method string, invalidBearer bool) *httptest.ResponseRecorder {
				r := httptest.NewRequest(method, "http://example.test"+path, nil)
				r.RemoteAddr = "192.0.2.1:1234"
				r.Header.Set("X-Forwarded-For", forwarded)
				if invalidBearer {
					r.Header.Set("Authorization", "invalid")
				}
				w := httptest.NewRecorder()
				api.ServeHTTP(w, r)
				return w
			}
			// Act
			first := request("/api/v1/wishlists", "198.51.100.1", "GET", false)
			second := request("/api/v1/wishlists", "198.51.100.2", "GET", true)
			// Assert
			if first.Code != 401 {
				t.Fatal("first request was not admitted")
			}
			want := 429
			if trusted {
				want = 401
			}
			if second.Code != want {
				t.Fatal("proxy quota or bearer order differs")
			}
			if !trusted && (second.Header().Get("Retry-After") != "60" || second.Header().Get("Cache-Control") != "no-store") {
				t.Fatal("quota headers differ")
			}
			if request("/liveness", "", "GET", false).Code != 200 || request("/openapi/v1.json", "", "GET", false).Code != 200 || request("/api/v1/wishlists", "", "OPTIONS", false).Code != 204 {
				t.Fatal("probe or preflight incorrectly consumes quota")
			}
			now = now.Add(time.Minute)
			if request("/api/v1/wishlists", "198.51.100.1", "GET", false).Code != 401 {
				t.Fatal("fixed window did not replenish")
			}
		})
	}
}
