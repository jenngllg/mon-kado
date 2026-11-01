//go:build integration

package httpapi_test

import (
	"context"
	"encoding/json"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

type previewFake struct {
	Context context.Context
	Calls   int
}

func (f *previewFake) Preview(ctx context.Context, source string) (domain.WishImportPreview, error) {
	if ctx != f.Context || source != "https://merchant.example/product" {
		return domain.WishImportPreview{}, context.Canceled
	}
	f.Calls++
	name, price := "Editable suggestion", json.Number("12.34")
	return domain.WishImportPreview{Name: &name, Url: &source, Price: &price, Quantity: 1, Warnings: []string{}}, nil
}

func TestImportPreview_WhenQuotaReached_RemainsPerMemberAndCreatesNoWish(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "firstimporter@example.test")
	var first domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Import list","occasion":"other"}`, nil), 201, &first)
	firstAccess := f.access
	api := f.server.Config.Handler.(*httpapi.API)
	provider := &previewFake{Context: t.Context()}
	api.Importer = provider
	preview := func(list, access string) *httptest.ResponseRecorder {
		t.Helper()
		request := httptest.NewRequest("POST", f.server.URL+"/api/v1/wishlists/"+list+"/wish-import-previews", strings.NewReader(`{"url":"https://merchant.example/product"}`)).WithContext(t.Context())
		request.Header.Set("Authorization", "Bearer "+access)
		request.Header.Set("Content-Type", "application/json")
		response := httptest.NewRecorder()
		api.ServeHTTP(response, request)
		return response
	}
	// Act
	for index := 0; index < 10; index++ {
		response := preview(first.Id, firstAccess)
		var result domain.WishImportPreview
		if response.Code != 200 || json.Unmarshal(response.Body.Bytes(), &result) != nil || result.Price == nil || *result.Price != "12.34" || result.Name == nil || *result.Name != "Editable suggestion" {
			t.Fatal("preview contract or context propagation differs")
		}
	}
	if response := preview(first.Id, firstAccess); response.Code != 429 || response.Header().Get("Retry-After") != "60" {
		t.Fatal("preview quota not enforced")
	}
	f.access = ""
	f.csrfToken(t)
	f.member(t, "secondimporter@example.test")
	var second domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Other import list","occasion":"other"}`, nil), 201, &second)
	if response := preview(first.Id, f.access); response.Code != 404 {
		t.Fatal("foreign wishlist accepted import")
	}
	if response := preview(second.Id, f.access); response.Code != 200 {
		t.Fatal("another member inherited the first member's quota")
	}
	// Assert
	if provider.Calls != 11 {
		t.Fatal("invalid, limited or foreign request contacted the provider")
	}
	secondAccess := f.access
	for _, list := range []domain.WishlistResponse{first, second} {
		access := firstAccess
		if list.Id == second.Id {
			access = secondAccess
		}
		f.access = access
		var wishes domain.WishCollectionResponse
		requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+list.Id+"/wishes", "", nil), 200, &wishes)
		if wishes.Wishes == nil || len(wishes.Wishes) != 0 {
			t.Fatal("preview created a persistent wish")
		}
	}
}
