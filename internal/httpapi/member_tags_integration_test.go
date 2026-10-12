//go:build integration

package httpapi_test

import (
	"fmt"
	"net/url"
	"regexp"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestMemberTags_WhenSameNormalizedNameRegistersConcurrently_AllocatesDistinctTags(t *testing.T) {
	// Arrange
	f := newFixture(t)
	const members = 12
	results := make(chan error, members)
	// Act: every request owns its DTO and uses the same caller context.
	for index := range members {
		go func() {
			name := []string{"Éloïse", "ÉLOÏSE", "E\u0301loi\u0308se"}[index%3]
			email, password := fmt.Sprintf("concurrent-tag-%d@example.test", index), "A long test password 🎁"
			results <- f.service.Register(t.Context(), domain.RegisterAccountRequest{DisplayName: &name, Email: &email, Password: &password})
		}()
	}
	for range members {
		if err := <-results; err != nil {
			t.Fatal(err)
		}
	}
	// Assert: direct persistence checks cover the concurrent uniqueness invariant.
	var count, tags, names int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*),count(DISTINCT display_name_tag),count(DISTINCT normalized_display_name) FROM users`).Scan(&count, &tags, &names); err != nil {
		t.Fatal(err)
	}
	if count != members || tags != members || names != 1 {
		t.Fatal("concurrent normalized names did not receive distinct tags")
	}
}

func TestMemberTags_WhenNamesAreRepeated_PreservesUniqueSearchableIdentityAcrossProfileChanges(t *testing.T) {
	// Arrange
	f := newFixture(t)
	first := f.member(t, "tag-first@example.test")
	var original domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &original)
	second := f.member(t, "tag-second@example.test")
	setMemberSearchVisible(t, f)
	var current domain.CurrentSessionResponse
	response := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	etag := response.Header.Get("ETag")
	requireStatus(t, response, 200, &current)
	// Assert: creation allocates tags without changing either display name.
	validTag := regexp.MustCompile(`^[0-9]{4}$`)
	if first == second || original.DisplayName != current.DisplayName || original.DisplayNameTag == current.DisplayNameTag ||
		!validTag.MatchString(original.DisplayNameTag) || !validTag.MatchString(current.DisplayNameTag) || original.DisplayNameTag == "0000" || current.DisplayNameTag == "0000" {
		t.Fatal("repeated names did not receive distinct four-digit tags")
	}
	var found domain.PaginatedResponse[domain.UserSearchResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/members?displayName="+url.QueryEscape(current.DisplayName+"#"+current.DisplayNameTag), "", nil), 200, &found)
	if found.TotalCount != 1 || len(found.Items) != 1 || found.Items[0].Id != second || found.Items[0].DisplayNameTag != current.DisplayNameTag {
		t.Fatal("full handle did not identify exactly the selected member")
	}
	// Act: renaming into an unused namespace retains the account's assigned tag.
	var profile domain.MemberProfileResponse
	updated := f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Renamed member","isVisibleInMemberSearch":false}`, map[string]string{"If-Match": etag})
	requireStatus(t, updated, 200, &profile)
	// Assert: exact handles do not bypass the existing search-visibility preference.
	if profile.DisplayNameTag != current.DisplayNameTag || profile.DisplayName != "Renamed member" {
		t.Fatal("profile change lost the assigned tag")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/members?displayName="+url.QueryEscape(profile.DisplayName+"#"+profile.DisplayNameTag), "", nil), 200, &found)
	if found.TotalCount != 0 || len(found.Items) != 0 {
		t.Fatal("full handle bypassed search visibility")
	}
	var public domain.PublicMemberProfileResponse
	access := f.access
	f.access = ""
	requireStatus(t, f.send(t, "GET", "/api/v1/members/"+second+"/profile", "", nil), 200, &public)
	f.access = access
	if public.DisplayName != profile.DisplayName || public.DisplayNameTag != profile.DisplayNameTag {
		t.Fatal("public identity diverged from the saved profile")
	}
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Stale name"}`, map[string]string{"If-Match": etag}), 412, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &current)
	if current.DisplayNameTag != profile.DisplayNameTag || current.DisplayName != profile.DisplayName {
		t.Fatal("session identity diverged from the saved profile")
	}
}

func TestMemberTags_WhenRenameConflicts_RejectsWithoutChangingIdentityAndAllowsAnotherName(t *testing.T) {
	// Arrange: seed two name namespaces with the same immutable tag, so the
	// collision is deterministic rather than dependent on random allocation.
	f := newFixture(t)
	f.member(t, "tag-conflict-first@example.test")
	var first domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &first)
	second := f.member(t, "tag-conflict-second@example.test")
	setMemberSearchVisible(t, f)
	tx, err := f.store.Pool.Begin(t.Context())
	if err != nil {
		t.Fatal(err)
	}
	defer tx.Rollback(t.Context())
	if _, err := tx.Exec(t.Context(), `ALTER TABLE users DISABLE TRIGGER users_assign_display_name_tag`); err != nil {
		t.Fatal(err)
	}
	if _, err := tx.Exec(t.Context(), `UPDATE users SET display_name='Different member',display_name_tag=$2 WHERE id=$1`, second, first.DisplayNameTag); err != nil {
		t.Fatal(err)
	}
	if _, err := tx.Exec(t.Context(), `ALTER TABLE users ENABLE TRIGGER users_assign_display_name_tag`); err != nil {
		t.Fatal(err)
	}
	if err := tx.Commit(t.Context()); err != nil {
		t.Fatal(err)
	}
	var current domain.CurrentSessionResponse
	session := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	etag := session.Header.Get("ETag")
	requireStatus(t, session, 200, &current)

	// Act
	var conflict map[string]any
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"TEST MEMBER","isVisibleInMemberSearch":false}`, map[string]string{"If-Match": etag}), 409, &conflict)

	// Assert: the stable error contract and persisted identity survive the failure.
	if len(conflict) != 5 || conflict["statusCode"] != float64(409) || conflict["errorCode"] != domain.MemberHandleAlreadyUsed || conflict["title"] == nil || conflict["message"] != "This display name is already used with your tag. Choose another display name." || conflict["validationErrors"] != nil {
		t.Fatal("rename conflict violated the error contract")
	}
	session = f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	if session.Header.Get("ETag") != etag {
		t.Fatal("rejected rename changed the profile version")
	}
	requireStatus(t, session, 200, &current)
	if current.DisplayName != "Different member" || current.DisplayNameTag != first.DisplayNameTag || !current.IsVisibleInMemberSearch {
		t.Fatal("rejected rename changed the saved identity")
	}
	var profile domain.MemberProfileResponse
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Available member"}`, map[string]string{"If-Match": etag}), 200, &profile)
	if profile.DisplayName != "Available member" || profile.DisplayNameTag != first.DisplayNameTag {
		t.Fatal("successful rename changed the immutable tag")
	}
}

func setMemberSearchVisible(t *testing.T, f *fixture) {
	t.Helper()
	var current domain.CurrentSessionResponse
	session := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	requireStatus(t, session, 200, &current)
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", jsonInput(t, map[string]any{"displayName": current.DisplayName, "isVisibleInMemberSearch": true}), map[string]string{"If-Match": session.Header.Get("ETag")}), 200, nil)
}

func TestMemberTags_WhenClientOrDatabaseAttemptsReplacement_PreservesAssignedTag(t *testing.T) {
	// Arrange
	f := newFixture(t)
	id := f.member(t, "immutable-tag@example.test")
	var original domain.CurrentSessionResponse
	session := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	requireStatus(t, session, 200, &original)
	// Act / Assert: ignored extension fields cannot choose an identity tag.
	var updated domain.MemberProfileResponse
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Renamed","displayNameTag":"0001"}`, map[string]string{"If-Match": session.Header.Get("ETag")}), 200, &updated)
	if updated.DisplayNameTag != original.DisplayNameTag {
		t.Fatal("client extension field changed the assigned tag")
	}
	// Direct persistence access verifies the trigger's immutable-account invariant.
	if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET display_name_tag='0000' WHERE id=$1`, id); err != nil {
		t.Fatal(err)
	}
	var current domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &current)
	if current.DisplayNameTag != original.DisplayNameTag || current.DisplayName != "Renamed" {
		t.Fatal("an attempted tag replacement changed the account identity")
	}
}
