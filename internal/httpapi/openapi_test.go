package httpapi_test

import (
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"io"
	"log/slog"
	"net/http/httptest"
	"testing"
	"time"
)

func TestOpenAPI_WhenReadingDocument_ContainsVersionedOperationsAndExactErrorEnvelope(t *testing.T) {
	// Arrange
	cfg := config.Config{AllowedHosts: []string{"example.com"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
	api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
	request := httptest.NewRequest("GET", "http://example.com/openapi/v1.json", nil)
	recorder := httptest.NewRecorder()
	// Act
	api.ServeHTTP(recorder, request)
	var document struct {
		OpenAPI    string                                `json:"openapi"`
		Paths      map[string]map[string]json.RawMessage `json:"paths"`
		Components struct {
			Schemas map[string]struct {
				Properties map[string]json.RawMessage `json:"properties"`
			} `json:"schemas"`
		} `json:"components"`
	}
	err := json.Unmarshal(recorder.Body.Bytes(), &document)
	// Assert
	if err != nil || recorder.Code != 200 || document.OpenAPI != "3.1.0" || len(api.Patterns()) != 82 {
		t.Fatalf("invalid specification: status %d patterns %d error %v", recorder.Code, len(api.Patterns()), err)
	}
	fields := document.Components.Schemas["ErrorResponse"].Properties
	if len(fields) != 5 {
		t.Fatalf("error envelope contains %d fields", len(fields))
	}
	for _, field := range []string{"statusCode", "title", "message", "errorCode", "validationErrors"} {
		if fields[field] == nil {
			t.Fatalf("missing %s", field)
		}
	}
	if len(document.Components.Schemas["ValidationError"].Properties) != 2 {
		t.Fatal("validation error contract changed")
	}
	for _, endpoint := range []struct{ Path, Method string }{{"/api/v1/auth/google/callback", "post"}, {"/api/v1/admin/audit-events", "get"}, {"/api/v1/members/current/data-exports/{exportId}/archive", "get"}, {"/api/v1/auth/two-factor/completions", "post"}} {
		if document.Paths[endpoint.Path][endpoint.Method] == nil {
			t.Fatalf("missing %s", endpoint.Path)
		}
	}
}
