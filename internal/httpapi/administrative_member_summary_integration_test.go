//go:build integration

package httpapi_test

import (
	"encoding/json"
	"fmt"
	"github.com/jackc/pgx/v5"
	"os"
	"testing"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
)

func administrativeSummary(t *testing.T, f *fixture, id string) application.AdministrativeMemberSummaryResponse {
	t.Helper()
	var page domain.PaginatedResponse[application.AdministrativeMemberSummaryResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members?pageSize=100", "", nil), 200, &page)
	for _, item := range page.Items {
		if item.ID == id {
			return item
		}
	}
	t.Fatal("account missing from authorized page")
	return application.AdministrativeMemberSummaryResponse{}
}

func TestAdministrativeMemberSummary_WhenResourcesExist_CountsOnlyOwnedListsWishesAndMemberships(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "counts@example.test")
	lists := []domain.WishlistResponse{}
	for index := range 2 {
		var list domain.WishlistResponse
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", fmt.Sprintf(`{"name":"List %d","occasion":"other"}`, index), nil), 201, &list)
		lists = append(lists, list)
	}
	for _, list := range lists {
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Wish","quantity":9}`, nil), 201, nil)
	}
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+lists[0].Id+"/wishes", `{"name":"Other wish"}`, nil), 201, nil)
	for range 2 {
		requireStatus(t, f.send(t, "POST", "/api/v1/groups", `{"name":"Group"}`, nil), 201, nil)
	}
	f.member(t, "other@example.test")
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Other list","occasion":"other"}`, nil), 201, nil)
	f.administrator(t, "admin-summary@example.test")
	// Act
	got := administrativeSummary(t, f, member)
	// Assert
	if got.ListCount != 2 || got.GroupCount != 2 || got.WishCount != 3 || got.CreatedAt.IsZero() || got.HasAddedViaExtension == nil || *got.HasAddedViaExtension {
		t.Fatalf("unexpected summary: %+v", got)
	}
	var detail map[string]any
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members/"+member, "", nil), 200, &detail)
	if len(detail) != 3 {
		t.Fatal("erasure identity contract changed")
	}
}

func TestAdministrativeMemberSummary_WhenExtensionCreatesThenDeletesWish_PreservesConfirmedUsageWithoutChangingProfileETag(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "extension@example.test")
	memberAccess := f.access
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"List","occasion":"other"}`, nil), 201, &list)
	path := "/api/v1/wishlists/" + list.Id + "/wishes"
	before := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	tag := before.Header.Get("ETag")
	requireStatus(t, before, 200, nil)
	requireStatus(t, f.send(t, "POST", path, `{"name":"","addedViaExtension":true}`, nil), 400, nil)
	requireStatus(t, f.send(t, "POST", path, `{"name":"Invalid flag","addedViaExtension":"true"}`, nil), 400, nil)
	f.administrator(t, "extension-admin@example.test")
	adminAccess := f.access
	if v := administrativeSummary(t, f, member).HasAddedViaExtension; v == nil || *v {
		t.Fatal("failed additions changed usage")
	}
	// Act
	f.access = memberAccess
	created := f.send(t, "POST", path, `{"name":"Extension wish","addedViaExtension":true}`, nil)
	wishTag := created.Header.Get("ETag")
	var wish domain.WishResponse
	requireStatus(t, created, 201, &wish)
	after := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	afterTag := after.Header.Get("ETag")
	requireStatus(t, after, 200, nil)
	requireStatus(t, f.send(t, "DELETE", path+"/"+wish.Id, "", map[string]string{"If-Match": wishTag}), 204, nil)
	f.access = adminAccess
	// Assert
	got := administrativeSummary(t, f, member)
	if got.HasAddedViaExtension == nil || !*got.HasAddedViaExtension || got.WishCount != 0 || afterTag != tag {
		t.Fatalf("usage or profile version changed incorrectly: %+v", got)
	}
	var exported struct {
		Account struct {
			Profile map[string]any `json:"profile"`
		} `json:"account"`
	}
	if err := json.Unmarshal([]byte(suggestionExportData(t, f, member)), &exported); err != nil {
		t.Fatal(err)
	}
	if exported.Account.Profile["hasAddedViaExtension"] != true {
		t.Fatal("own export omitted confirmed extension usage")
	}

}

func TestAdministrativeMemberSummary_WhenLegacyUsageIsUnknown_DoesNotInventAnExtensionHistory(t *testing.T) {
	// Arrange: the migration preserves a NULL row for every pre-existing account.
	f := newFixture(t)
	member := f.member(t, "legacy-extension@example.test")
	migration, err := os.ReadFile("../persistence/migrations/029_member_extension_usage.sql")
	if err != nil {
		t.Fatal(err)
	}
	if err = f.store.Transaction(t.Context(), func(tx pgx.Tx) error {
		if _, err := tx.Exec(t.Context(), `DROP TABLE member_extension_usage`); err != nil {
			return err
		}
		_, err := tx.Exec(t.Context(), string(migration))
		return err
	}); err != nil {
		t.Fatal(err)
	}
	f.administrator(t, "legacy-admin@example.test")
	// Act / Assert
	if got := administrativeSummary(t, f, member); got.HasAddedViaExtension != nil {
		t.Fatal("unknown usage was presented as a confirmed boolean")
	}
	var exported struct {
		Account struct {
			Profile map[string]any `json:"profile"`
		} `json:"account"`
	}
	if err := json.Unmarshal([]byte(suggestionExportData(t, f, member)), &exported); err != nil {
		t.Fatal(err)
	}
	if value, exists := exported.Account.Profile["hasAddedViaExtension"]; !exists || value != nil {
		t.Fatal("legacy export invented extension history")
	}

}

func TestAdministrativeMemberSummary_WhenExtensionAddsAnIdea_AttributesOnlyItsAuthor(t *testing.T) {
	// Arrange
	s, _, collection := suggestionScenario(t)
	// Act
	requireStatus(t, s.member.send(t, "POST", collection, `{"name":"Extension idea","addedViaExtension":true}`, nil), 201, nil)
	s.owner.administrator(t, "idea-summary-admin@example.test")
	// Assert
	author := administrativeSummary(t, s.owner, s.memberID)
	owner := administrativeSummary(t, s.owner, s.ownerID)
	if author.HasAddedViaExtension == nil || !*author.HasAddedViaExtension || owner.HasAddedViaExtension == nil || *owner.HasAddedViaExtension {
		t.Fatal("extension usage was attributed to the wrong account")
	}
}
