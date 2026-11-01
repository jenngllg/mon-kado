//go:build integration

package persistence_test

import (
	"testing"

	"github.com/JennGllg/mon-kado/internal/security"
)

func TestMigrate_WhenMemberSearchDefaultWasHidden_EnablesNewMembersAndPreservesSavedChoices(t *testing.T) {
	// Arrange: simulate an earlier schema with an opt-in default.
	store := migrationStore(t)
	if err := store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}
	if _, err := store.Pool.Exec(t.Context(), `ALTER TABLE users ALTER COLUMN is_visible_in_member_search SET DEFAULT false; DELETE FROM mon_kado_migrations WHERE name='017_member_search_visible_by_default.sql'`); err != nil {
		t.Fatal(err)
	}
	insert := `INSERT INTO users(id,display_name,created_at,user_name,normalized_user_name,email,normalized_email,email_confirmed,security_stamp,concurrency_stamp,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count,is_visible_in_member_search) VALUES($1::uuid,$1::text,now(),$1::text,$1::text,$1::text,$1::text,true,$1::text,$1::text,false,false,true,0,$2)`
	hidden, visible := security.NewID(), security.NewID()
	for _, member := range []struct {
		id      string
		visible bool
	}{{hidden, false}, {visible, true}} {
		if _, err := store.Pool.Exec(t.Context(), insert, member.id, member.visible); err != nil {
			t.Fatal(err)
		}
	}
	// Act: both the forward migration and a second run must preserve preferences.
	for range 2 {
		if err := store.Migrate(t.Context()); err != nil {
			t.Fatal(err)
		}
	}
	created := security.NewID()
	if _, err := store.Pool.Exec(t.Context(), `INSERT INTO users(id,display_name,created_at,user_name,normalized_user_name,email,normalized_email,email_confirmed,security_stamp,concurrency_stamp,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) VALUES($1::uuid,$1::text,now(),$1::text,$1::text,$1::text,$1::text,true,$1::text,$1::text,false,false,true,0)`, created); err != nil {
		t.Fatal(err)
	}
	// Assert: SQL defaults are a persistence invariant, including for Google accounts.
	for _, member := range []struct {
		id      string
		visible bool
	}{{hidden, false}, {visible, true}, {created, true}} {
		var actual bool
		if err := store.Pool.QueryRow(t.Context(), `SELECT is_visible_in_member_search FROM users WHERE id=$1`, member.id).Scan(&actual); err != nil || actual != member.visible {
			t.Fatal("migration changed a saved preference or kept the hidden account default")
		}
	}
}
