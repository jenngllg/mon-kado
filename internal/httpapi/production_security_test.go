package httpapi_test

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

func TestProductionSecurity_WhenServingRequests_HidesDocumentationAndIncludesSubdomains(t *testing.T) {
	for _, production := range []bool{false, true} {
		name := "local"
		if production {
			name = "production"
		}
		t.Run(name, func(t *testing.T) {
			// Arrange
			cfg := config.Config{Production: production, AllowedHosts: []string{"example.com"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			for _, test := range []struct {
				method, path string
				status       int
			}{
				{"GET", "/openapi/v1.json", 200},
				{"HEAD", "/openapi/v1.json", 200},
				{"GET", "/liveness", 200},
				{"GET", "/api/v1/wishlists", 401},
				{"GET", "/does-not-exist", 404},
			} {
				t.Run(test.method+test.path, func(t *testing.T) {
					request := httptest.NewRequest(test.method, "https://example.com"+test.path, nil)
					recorder := httptest.NewRecorder()
					expectedStatus, expectedHSTS := test.status, ""
					if production {
						expectedHSTS = "max-age=31536000; includeSubDomains"
						if test.path == "/openapi/v1.json" {
							expectedStatus = 404
						}
					}

					// Act
					api.ServeHTTP(recorder, request)

					// Assert
					if recorder.Code != expectedStatus || recorder.Header().Get("Strict-Transport-Security") != expectedHSTS {
						t.Fatalf("security policy differs: status=%d HSTS=%q", recorder.Code, recorder.Header().Get("Strict-Transport-Security"))
					}
					if production && test.path == "/openapi/v1.json" {
						var failure domain.ErrorResponse
						if err := json.Unmarshal(recorder.Body.Bytes(), &failure); err != nil || failure.ErrorCode == nil || *failure.ErrorCode != domain.RequestNotFound {
							t.Fatal("production documentation must return the ordinary not-found envelope")
						}
					}
				})
			}
		})
	}
}
