package httpapi_test

import (
	"bytes"
	"io"
	"log/slog"
	"net/http/httptest"
	"net/netip"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

func TestPerimeter_WhenKnownRouteIsRejected_LogsOnlyItsTemplateAndCorrelation(t *testing.T) {
	// Arrange
	var logs bytes.Buffer
	cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32), PerimeterOptions: config.PerimeterOptions{PermitLimit: 1, Window: time.Minute}}
	api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(&logs, nil)))
	for range 2 {
		r := httptest.NewRequest("GET", "http://example.test/api/v1/shared-wishlists/private-resource?token=private-secret", nil).WithContext(t.Context())
		api.ServeHTTP(httptest.NewRecorder(), r)
	}
	// Assert
	text := logs.String()
	if !strings.Contains(text, `"Operation":"GET /api/v1/shared-wishlists/{shareLinkId}"`) || !strings.Contains(text, `"StatusCode":429`) || !strings.Contains(text, `"CorrelationId"`) {
		t.Fatal("early rejection omitted its safe operation or correlation")
	}
	if strings.Contains(text, "private-resource") || strings.Contains(text, "private-secret") {
		t.Fatal("early rejection logged a concrete resource or query credential")
	}
}

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

func TestPerimeter_WhenImageReadsReachTheirBudget_PreservesAPIAccessAcrossMediaRoutes(t *testing.T) {
	for _, path := range []string{
		"/api/v1/wishlists/invalid/wishes/invalid/image",
		"/api/v1/shared-wishlists/invalid/wishes/invalid/image",
		"/api/v1/members/invalid/profile/image",
		"/api/v1/groups/invalid/image",
		"/api/v1/groups/invalid/preview/image",
		"/api/v1/groups/invalid/wishlists/invalid/wishes/invalid/image",
		"/api/v1/groups/invalid/wishlists/invalid/suggestions/invalid/image/content",
		"/api/v1/shared-wishlists/invalid/preview/image",
	} {
		t.Run(path, func(t *testing.T) {
			// Arrange
			cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32), PerimeterOptions: config.PerimeterOptions{PermitLimit: 1, ImageReadPermitLimit: 2, Window: time.Minute}}
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			request := func(target string) int {
				r := httptest.NewRequest("GET", "http://example.test"+target, nil).WithContext(t.Context())
				w := httptest.NewRecorder()
				api.ServeHTTP(w, r)
				return w.Code
			}
			// Act / Assert
			for range 2 {
				if request(path) == 429 {
					t.Fatal("image read consumed the API allowance")
				}
			}
			if request(path) != 429 {
				t.Fatal("image allowance was not enforced")
			}
			if request("/api/v1/wishlists") != 401 {
				t.Fatal("image traffic blocked the next API action")
			}
			if request("/api/v1/wishlists") != 429 {
				t.Fatal("API allowance was not enforced independently")
			}
		})
	}
}
