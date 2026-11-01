package httpapi_test

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http/httptest"
	"reflect"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

func TestPublicMemberProfile_WhenIdentifierInvalid_RejectsBeforePersistence(t *testing.T) {
	for _, scenario := range []struct {
		name, id string
		status   int
	}{
		{"empty", "00000000-0000-0000-0000-000000000000", 400},
		{"malformed", "not-a-member-id", 404},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange: a nil store proves that no persistence call is made.
			cfg := config.Config{AllowedHosts: []string{"example.com"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			recorder := httptest.NewRecorder()
			request := httptest.NewRequest("GET", "http://example.com/api/v1/members/"+scenario.id+"/profile", nil)
			// Act
			api.ServeHTTP(recorder, request)
			// Assert
			if recorder.Code != scenario.status || recorder.Header().Get("Cache-Control") != "no-store" {
				t.Fatalf("unexpected invalid identifier response: %d", recorder.Code)
			}
			var result domain.ErrorResponse
			if err := json.Unmarshal(recorder.Body.Bytes(), &result); err != nil {
				t.Fatal(err)
			}
			if scenario.status == 400 && (result.ErrorCode == nil || *result.ErrorCode != domain.RequestValidationError || len(result.ValidationErrors) != 1 || result.ValidationErrors[0].PropertyName != "memberId") {
				t.Fatal("empty identifier validation contract changed")
			}
			if scenario.status == 400 {
				for range 59 {
					attempt := httptest.NewRecorder()
					api.ServeHTTP(attempt, request.Clone(t.Context()))
					if attempt.Code != 400 {
						t.Fatal("empty identifier request unexpectedly succeeded")
					}
				}
				limited := httptest.NewRecorder()
				api.ServeHTTP(limited, request.Clone(t.Context()))
				if limited.Code != 429 || limited.Header().Get("Retry-After") != "60" {
					t.Fatal("empty identifier bypassed the public profile quota")
				}
			}
		})
	}
}

func TestPublicMemberProfile_WhenDocumented_PreservesAnonymousMinimalContract(t *testing.T) {
	// Arrange
	api := httpapi.New(application.New(nil, config.Config{AllowedHosts: []string{"example.com"}}, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
	recorder := httptest.NewRecorder()
	// Act
	api.ServeHTTP(recorder, httptest.NewRequest("GET", "http://example.com/openapi/v1.json", nil))
	var document struct {
		Paths map[string]map[string]struct {
			Security  []any                      `json:"security"`
			Responses map[string]json.RawMessage `json:"responses"`
		} `json:"paths"`
		Components struct {
			Schemas map[string]struct {
				Properties map[string]json.RawMessage `json:"properties"`
				Required   []string                   `json:"required"`
			} `json:"schemas"`
		} `json:"components"`
	}
	if err := json.Unmarshal(recorder.Body.Bytes(), &document); err != nil {
		t.Fatal(err)
	}
	// Assert
	op := document.Paths["/api/v1/members/{memberId}/profile"]["get"]
	if op.Security == nil || len(op.Security) != 0 || op.Responses["401"] != nil {
		t.Fatal("public profile unexpectedly requires authentication")
	}
	for _, status := range []string{"200", "400", "404", "429", "503", "500"} {
		if op.Responses[status] == nil {
			t.Fatalf("missing response %s", status)
		}
	}
	for schema, fields := range map[string][]string{
		"PublicMemberProfileResponse":  {"id", "displayName", "displayNameTag", "profileImageUrl", "wishlists"},
		"PublicMemberWishlistResponse": {"id", "name", "occasion", "eventDate", "shareUrl"},
	} {
		actual := document.Components.Schemas[schema]
		required := fields
		if schema == "PublicMemberProfileResponse" {
			// The name tag is additive during the existing clients' rollout.
			required = []string{"id", "displayName", "profileImageUrl", "wishlists"}
		}
		if len(actual.Properties) != len(fields) || !reflect.DeepEqual(actual.Required, required) {
			t.Fatalf("unexpected %s schema", schema)
		}
		for _, field := range fields {
			if actual.Properties[field] == nil {
				t.Fatalf("missing %s.%s", schema, field)
			}
		}
	}
}
