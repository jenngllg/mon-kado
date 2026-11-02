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
	if err != nil || recorder.Code != 200 || document.OpenAPI != "3.1.0" || len(api.Patterns()) != 159 {
		t.Fatalf("invalid specification: status %d patterns %d error %v", recorder.Code, len(api.Patterns()), err)
	}
	if _, exists := document.Paths["/api/v1/wishlists/{wishlistId}/wish-import-previews"]; !exists {
		t.Fatal("existing import endpoint is missing from the contract")
	}
	fields := document.Components.Schemas["ErrorResponse"].Properties
	for _, path := range []string{"/api/v1/admin/members", "/api/v1/admin/members/{memberId}"} {
		var operation struct {
			Security []map[string]json.RawMessage `json:"security"`
		}
		if err := json.Unmarshal(document.Paths[path]["get"], &operation); err != nil || len(operation.Security) != 1 || operation.Security[0]["bearerAuth"] == nil {
			t.Fatalf("administrative member read lacks bearer security: %s", path)
		}
	}
	identity := document.Components.Schemas["AdministrativeMemberResponse"].Properties
	if len(identity) != 3 || identity["id"] == nil || identity["displayName"] == nil || identity["displayNameTag"] == nil {
		t.Fatal("administrative identity must not expose additional account data")
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
	for _, field := range []struct{ schema, name string }{{"CurrentSessionResponse", "isGoogleLinked"}, {"CurrentSessionResponse", "isVisibleInMemberSearch"}, {"MemberProfileResponse", "isVisibleInMemberSearch"}, {"SharedWishlistResponse", "canSubscribe"}} {
		var property struct {
			Type string `json:"type"`
		}
		if json.Unmarshal(document.Components.Schemas[field.schema].Properties[field.name], &property) != nil || property.Type != "boolean" {
			t.Fatalf("missing boolean %s.%s", field.schema, field.name)
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
	}
	if json.Unmarshal(document.Paths["/api/v1/shared-wishlists/{shareLinkId}/participants"]["post"], &join) != nil || join.RequestBody.Required || join.RequestBody.Content["application/json"].Schema.Ref != "#/components/schemas/JoinSharedWishlistRequest" || document.Components.Schemas["JoinSharedWishlistRequest"].Properties["displayName"] == nil {
		t.Fatal("guest display-name request must be documented and optional for members")
	}
	for _, endpoint := range []struct{ Path, Method string }{{"/api/v1/auth/google/callback", "post"}, {"/api/v1/admin/audit-events", "get"}, {"/api/v1/members/current/data-exports/{exportId}/archive", "get"}, {"/api/v1/auth/two-factor/completions", "post"}} {
		if document.Paths[endpoint.Path][endpoint.Method] == nil {
			t.Fatalf("missing %s", endpoint.Path)
		}
	}
	for _, path := range []string{"/api/v1/groups/join", "/api/v1/groups/{groupId}/preview", "/api/v1/groups/{groupId}/preview/image"} {
		if document.Paths[path]["get"] == nil {
			t.Fatalf("missing group invitation preview contract %s", path)
		}
	}
	if len(document.Components.Schemas["GroupJoinPreviewResponse"].Properties) != 4 {
		t.Fatal("group preview must expose only its advertised identity")
	}
	visibility := "/api/v1/wishlists/{wishlistId}/group-visibility"
	if document.Paths[visibility]["get"] == nil || document.Paths[visibility]["put"] == nil || len(document.Components.Schemas["WishlistGroupVisibilityResponse"].Properties) != 3 {
		t.Fatal("missing bounded owner group visibility contract")
	}
	if document.Paths["/api/v1/wishlists/{wishlistId}/wishes/{wishId}"]["patch"] == nil || document.Components.Schemas["UpdateWishFavoriteRequest"].Properties["isFavorite"] == nil {
		t.Fatal("favorite mutation contract missing")
	}
	for _, schema := range []string{"SharedWishlistResponse", "SharedWishDetailResponse", "WishlistResponse"} {
		field := "canReserve"
		if schema == "WishlistResponse" {
			field = "surpriseMode"
		}
		var property struct {
			Type string `json:"type"`
		}
		if json.Unmarshal(document.Components.Schemas[schema].Properties[field], &property) != nil || property.Type != "boolean" {
			t.Fatalf("missing owner reading permission or surprise mode in %s", schema)
		}
	}
	for _, schema := range []string{"SharedWishResponse", "SharedWishDetailResponse"} {
		for _, field := range []string{"reservedQuantity", "availableQuantity", "currentParticipantReservedQuantity"} {
			var property struct {
				AnyOf []struct {
					Type string `json:"type"`
				} `json:"anyOf"`
			}
			if json.Unmarshal(document.Components.Schemas[schema].Properties[field], &property) != nil || len(property.AnyOf) != 2 || property.AnyOf[0].Type != "integer" || property.AnyOf[1].Type != "null" {
				t.Fatalf("surprise-preserving quantities must be explicitly nullable: %s.%s", schema, field)
			}
		}
	}
	for _, method := range []string{"get", "put", "delete"} {
		if document.Paths["/api/v1/wishlists/{wishlistId}/wishes/{wishId}/reservations/current"][method] == nil {
			t.Fatalf("missing owner reservation operation %s", method)
		}
	}
	suggestions := "/api/v1/groups/{groupId}/wishlists/{wishlistId}/suggestions"
	for _, operation := range []struct{ path, method string }{
		{suggestions, "get"}, {suggestions, "post"}, {suggestions + "/{suggestionId}", "get"}, {suggestions + "/{suggestionId}", "put"}, {suggestions + "/{suggestionId}", "delete"},
		{suggestions + "/{suggestionId}/image", "get"}, {suggestions + "/{suggestionId}/image", "put"}, {suggestions + "/{suggestionId}/image", "delete"},
		{suggestions + "/{suggestionId}/reservations/current", "get"}, {suggestions + "/{suggestionId}/reservations/current", "put"}, {suggestions + "/{suggestionId}/reservations/current", "delete"},
	} {
		if document.Paths[operation.path][operation.method] == nil {
			t.Fatalf("missing suggestion operation %s %s", operation.method, operation.path)
		}
	}
	for _, field := range []string{"id", "name", "note", "url", "price", "quantity", "reservedQuantity", "currentParticipantReservedQuantity", "authorId", "authorDisplayName", "canEdit", "hasImage", "createdAt", "updatedAt", "entityTag"} {
		if document.Components.Schemas["GroupSuggestionResponse"].Properties[field] == nil {
			t.Fatalf("missing suggestion field %s", field)
		}
	}
}
