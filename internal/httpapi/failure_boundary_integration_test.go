//go:build integration

package httpapi_test

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"net"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/jackc/pgx/v5/pgconn"
)

type failingPreview struct {
	Context context.Context
	Error   error
	Panic   bool
	Calls   int
}

func (f *failingPreview) Preview(ctx context.Context, source string) (domain.WishImportPreview, error) {
	f.Calls++
	if ctx != f.Context {
		return domain.WishImportPreview{}, errors.New("request context was substituted")
	}
	if f.Panic {
		panic("private-provider-details")
	}
	return domain.WishImportPreview{}, f.Error
}

func TestFailureBoundary_WhenProviderFails_MapsStatusAndLogsTechnicalFailureOnce(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "boundary@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Boundary list","occasion":"other"}`, nil), 201, &list)
	api := f.server.Config.Handler.(*httpapi.API)
	for _, tc := range []struct {
		name    string
		failure error
		panic   bool
		status  int
		log     string
	}{
		{"unexpected", errors.New("private-provider-details"), false, 500, "Unhandled request failure"},
		{"network", &net.DNSError{Err: "private-provider-details", Name: "private-host", IsTimeout: true}, false, 503, "Unexpected dependency failure"},
		{"databaseUnavailable", &pgconn.PgError{Code: "08006", Message: "private-provider-details"}, false, 503, "Unexpected dependency failure"},
		{"unexpectedDatabase", &pgconn.PgError{Code: "XX000", Message: "private-provider-details"}, false, 500, "Unhandled request failure"},
		{"panic", nil, true, 500, "Unhandled request panic"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			var logs bytes.Buffer
			api.Logger = slog.New(slog.NewJSONHandler(&logs, nil))
			provider := &failingPreview{Context: t.Context(), Error: tc.failure, Panic: tc.panic}
			api.Importer = provider
			request := httptest.NewRequest("POST", f.server.URL+"/api/v1/wishlists/"+list.Id+"/wish-import-previews", strings.NewReader(`{"url":"https://merchant.example/product"}`)).WithContext(t.Context())
			request.Header.Set("Authorization", "Bearer "+f.access)
			request.Header.Set("Content-Type", "application/json")
			response := httptest.NewRecorder()
			// Act
			api.ServeHTTP(response, request)
			// Assert
			var envelope domain.ErrorResponse
			if response.Code != tc.status || json.Unmarshal(response.Body.Bytes(), &envelope) != nil || envelope.StatusCode != tc.status || provider.Calls != 1 {
				t.Fatal("failure boundary contract differs")
			}
			if strings.Contains(response.Body.String(), "private-provider-details") || strings.Contains(logs.String(), "private-provider-details") || strings.Contains(logs.String(), "boundary@example.test") {
				t.Fatal("failure boundary disclosed private data")
			}
			if strings.Count(logs.String(), `"msg":"`+tc.log+`"`) != 1 || strings.Count(logs.String(), `"level":"ERROR"`) != 1 || !strings.Contains(logs.String(), `"StackTrace":`) {
				t.Fatal("technical exception was not logged exactly once with its stack")
			}
			if tc.status == 500 && envelope.ErrorCode != nil {
				t.Fatal("unexpected failure acquired a fabricated business code")
			}
			if tc.status == 503 && (envelope.ErrorCode == nil || *envelope.ErrorCode != domain.TechnicalDependencyUnavailable) {
				t.Fatal("dependency failure lost its stable code")
			}
		})
	}
}
