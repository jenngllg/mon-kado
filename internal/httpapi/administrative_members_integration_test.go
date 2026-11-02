//go:build integration

package httpapi_test

import (
	"net/url"
	"slices"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestAdministrativeMembers_WhenListingPages_ReturnsNewestAccountsFirst(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	oldest := f.administrator(t, "oldest@example.test")
	adminAccess := f.access
	now = now.Add(time.Second)
	middle := f.member(t, "middle@example.test")
	now = now.Add(time.Second)
	latest := []string{f.member(t, "latest-a@example.test"), f.member(t, "latest-b@example.test")}
	slices.Sort(latest)
	slices.Reverse(latest)
	want := append(latest, middle, oldest)
	f.access = adminAccess

	for _, query := range []string{"", "&q=" + url.QueryEscape("TeST MEmber")} {
		// Act
		var first, second domain.PaginatedResponse[application.AdministrativeMemberResponse]
		requireStatus(t, f.send(t, "GET", "/api/v1/admin/members?pageSize=2"+query, "", nil), 200, &first)
		requireStatus(t, f.send(t, "GET", "/api/v1/admin/members?pageSize=2&page=2"+query, "", nil), 200, &second)
		// Assert
		if first.TotalCount != 4 || first.TotalPages != 2 || !first.HasNextPage || !second.HasPreviousPage {
			t.Fatalf("unexpected pagination: %+v, %+v", first, second)
		}
		var got []string
		for _, item := range append(first.Items, second.Items...) {
			got = append(got, item.ID)
		}
		if !slices.Equal(got, want) {
			t.Fatalf("accounts should be newest first across pages: got %v, want %v", got, want)
		}
	}
}

func TestAdministrativeMembers_WhenSearchingHiddenAccounts_PreservesMinimalContractAndPagination(t *testing.T) {
	// Arrange
	f := newFixture(t)
	target := f.member(t, "hidden@example.test")
	if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET display_name='Élodie privée',is_visible_in_member_search=false,email_confirmed=false WHERE id=$1`, target); err != nil {
		t.Fatal(err)
	}
	f.administrator(t, "admin@example.test")
	// Act
	var page domain.PaginatedResponse[application.AdministrativeMemberResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members?q=elodie&pageSize=1", "", nil), 200, &page)
	// Assert
	if len(page.Items) != 1 || page.Items[0].ID != target || page.TotalCount != 1 || page.PageSize != 1 {
		t.Fatalf("hidden account missing: %+v", page)
	}
	var detail map[string]any
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members/"+target, "", nil), 200, &detail)
	if len(detail) != 3 || detail["id"] != target || detail["displayName"] != "Élodie privée" || detail["displayNameTag"] == nil {
		t.Fatalf("unexpected account identity contract: %+v", detail)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members?q="+url.QueryEscape("#"+page.Items[0].DisplayNameTag), "", nil), 200, &page)
	if len(page.Items) != 1 || page.Items[0].ID != target {
		t.Fatal("tag search failed")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members?q=elodie&page=3&pageSize=1", "", nil), 200, &page)
	if len(page.Items) != 0 || page.CurrentPage != 3 || page.TotalCount != 1 || page.TotalPages != 1 {
		t.Fatalf("wrong beyond-page metadata: %+v", page)
	}
	for _, query := range []string{"q=&q=duplicate", "q=" + url.QueryEscape(string(make([]byte, 101))), "page=0", "pageSize=101"} {
		requireStatus(t, f.send(t, "GET", "/api/v1/admin/members?"+query, "", nil), 400, nil)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members/"+security.NewID(), "", nil), 404, nil)
}

func TestAdministrativeMembers_WhenErasingAccount_RemovesFromFreshListAndDetail(t *testing.T) {
	// Arrange
	f := newFixture(t)
	target := f.member(t, "target@example.test")
	targetAccess := f.access
	f.administrator(t, "admin@example.test")
	var member application.AdministrativeMemberResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members/"+target, "", nil), 200, &member)
	// Act
	input := jsonInput(t, map[string]string{"confirmedMemberId": target, "requestReference": "SUPPORT-UI"})
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+target+"/erasure-requests", input, nil), 204, nil)
	// Assert
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members/"+target, "", nil), 404, nil)
	var page domain.PaginatedResponse[application.AdministrativeMemberResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/members?q="+url.QueryEscape(member.DisplayName+" #"+member.DisplayNameTag), "", nil), 200, &page)
	if len(page.Items) != 0 || page.TotalCount != 0 || page.TotalPages != 0 || page.HasNextPage || page.HasPreviousPage {
		t.Fatalf("erased account remains visible: %+v", page)
	}
	var audit domain.PaginatedResponse[domain.AdministrativeAuditEventResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/audit-events?requestReference=SUPPORT-UI", "", nil), 200, &audit)
	if audit.TotalCount != 1 {
		t.Fatal("erasure audit missing")
	}
	var queued int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM account_erasure_email_outbox`).Scan(&queued); err != nil {
		t.Fatal(err)
	}
	if queued != 1 {
		t.Fatal("confirmation notification missing")
	}
	f.access = targetAccess
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 401, nil)
}
