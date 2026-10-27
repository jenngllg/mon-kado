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
	if err != nil || recorder.Code != 200 || document.OpenAPI != "3.1.0" || len(api.Patterns()) != 138 {
		t.Fatalf("invalid specification: status %d patterns %d error %v", recorder.Code, len(api.Patterns()), err)
	}
	operationIDs := make(map[string]string)
	for path, methods := range document.Paths {
		for method, raw := range methods {
			var operation struct {
				ID string `json:"operationId"`
			}
			if err := json.Unmarshal(raw, &operation); err != nil {
				t.Fatalf("invalid operation for %s %s", method, path)
			}
			if operation.ID == "" {
				continue
			}
			if previous, exists := operationIDs[operation.ID]; exists {
				t.Fatalf("duplicate operation identifier %s for %s and %s", operation.ID, previous, path)
			}
			operationIDs[operation.ID] = path
		}
	}
	fields := document.Components.Schemas["ErrorResponse"].Properties
	var author struct {
		Type   string `json:"type"`
		Format string `json:"format"`
	}
	if json.Unmarshal(document.Components.Schemas["SharedWishlistResponse"].Properties["ownerId"], &author) != nil || author.Type != "string" || author.Format != "uuid" {
		t.Fatal("shared-list author identity is not documented")
	}
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
	for _, field := range []struct{ schema, name string }{{"CurrentSessionResponse", "isGoogleLinked"}, {"CurrentSessionResponse", "isVisibleInMemberSearch"}, {"MemberProfileResponse", "isVisibleInMemberSearch"}} {
		var property struct {
			Type string `json:"type"`
		}
		if json.Unmarshal(document.Components.Schemas[field.schema].Properties[field.name], &property) != nil || property.Type != "boolean" {
			t.Fatalf("missing boolean %s.%s", field.schema, field.name)
		}
	}
	for _, endpoint := range []struct{ Path, Method string }{{"/api/v1/auth/google/callback", "post"}, {"/api/v1/admin/audit-events", "get"}, {"/api/v1/members/current/data-exports/{exportId}/archive", "get"}, {"/api/v1/auth/two-factor/completions", "post"}} {
		if document.Paths[endpoint.Path][endpoint.Method] == nil {
			t.Fatalf("missing %s", endpoint.Path)
		}
	}
	var join struct {
		RequestBody struct {
			Required bool `json:"required"`
			Content  map[string]struct {
				Schema struct {
					Ref string `json:"$ref"`
				} `json:"schema"`
			} `json:"content"`
		} `json:"requestBody"`
		Responses map[string]struct {
			Headers map[string]json.RawMessage `json:"headers"`
		} `json:"responses"`
	}
	if json.Unmarshal(document.Paths["/api/v1/shared-wishlists/{shareLinkId}/participants"]["post"], &join) != nil || join.RequestBody.Required || join.RequestBody.Content["application/json"].Schema.Ref != "#/components/schemas/JoinSharedWishlistRequest" {
		t.Fatal("optional participant details are undocumented")
	}
	if document.Components.Schemas["JoinSharedWishlistRequest"].Properties["displayName"] == nil || join.Responses["201"].Headers["Location"] == nil || join.Responses["200"].Headers["ETag"] != nil || join.Responses["201"].Headers["ETag"] != nil {
		t.Fatal("participant input or response headers changed")
	}
}
