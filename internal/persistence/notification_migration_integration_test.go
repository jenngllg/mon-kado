//go:build integration

package persistence_test

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5/pgconn"
)

func TestMigrate_WhenNotificationExpansionWaitsForLiveReader_AbortsAtomicallyAndPreservesExistingData(t *testing.T) {
	// Arrange: the exact prior catalog, with real rows and a live legacy read.
	store := migrationStore(t)
	if _, err := store.Pool.Exec(t.Context(), `CREATE TABLE mon_kado_migrations(name text PRIMARY KEY,applied_at timestamptz NOT NULL DEFAULT now())`); err != nil {
		t.Fatal(err)
	}
	files, err := os.ReadDir("migrations")
	if err != nil {
		t.Fatal(err)
	}
	for _, file := range files {
		if file.Name() >= "027_member_notifications.sql" {
			continue
		}
		body, err := os.ReadFile(filepath.Join("migrations", file.Name()))
		if err != nil {
			t.Fatal(err)
		}
		if _, err = store.Pool.Exec(t.Context(), string(body)); err != nil {
			t.Fatal(err)
		}
		if _, err = store.Pool.Exec(t.Context(), `INSERT INTO mon_kado_migrations(name) VALUES($1)`, file.Name()); err != nil {
			t.Fatal(err)
		}
	}
	member, wishlist, now := security.NewID(), security.NewID(), time.Now().UTC()
	if _, err = store.Pool.Exec(t.Context(), `INSERT INTO users(id,display_name,display_name_tag,created_at,updated_at,user_name,normalized_user_name,email,normalized_email,email_confirmed,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) VALUES($1,'Migration member','1234',$2,$2,'migration@example.test','MIGRATION@EXAMPLE.TEST','migration@example.test','MIGRATION@EXAMPLE.TEST',true,false,false,true,0)`, member, now); err != nil {
		t.Fatal(err)
	}
	if _, err = store.Pool.Exec(t.Context(), `INSERT INTO wishlists(id,owner_id,name,normalized_name,occasion,created_at) VALUES($1,$2,'Existing list','EXISTING LIST','Other',$3)`, wishlist, member, now); err != nil {
		t.Fatal(err)
	}
	reader, err := store.Pool.Begin(t.Context())
	if err != nil {
		t.Fatal(err)
	}
	defer reader.Rollback(t.Context())
	var name string
	if err = reader.QueryRow(t.Context(), `SELECT name FROM wishlists WHERE id=$1`, wishlist).Scan(&name); err != nil {
		t.Fatal(err)
	}

	// Act: the transaction-local lock deadline cancels the complete expansion.
	err = store.Migrate(t.Context())
	var postgres *pgconn.PgError
	if !errors.As(err, &postgres) || postgres.Code != "55P03" {
		t.Fatalf("expected bounded lock refusal, got %T", err)
	}
	var table *string
	var applied bool
	if err = store.Pool.QueryRow(t.Context(), `SELECT to_regclass('member_notifications')::text,EXISTS(SELECT 1 FROM mon_kado_migrations WHERE name='027_member_notifications.sql')`).Scan(&table, &applied); err != nil || table != nil || applied {
		t.Fatal("failed expansion left partial schema or an applied ledger entry")
	}
	if err = reader.QueryRow(t.Context(), `SELECT name FROM wishlists WHERE id=$1`, wishlist).Scan(&name); err != nil || name != "Existing list" {
		t.Fatal("legacy reader or existing data changed")
	}
	if err = reader.Rollback(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err = store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err = store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}

	// Assert: one committed expansion; legacy reads and data remain intact.
	var dismissed bool
	if err = store.Pool.QueryRow(t.Context(), `SELECT name,sharing_reminder_dismissed FROM wishlists WHERE id=$1`, wishlist).Scan(&name, &dismissed); err != nil || name != "Existing list" || dismissed {
		t.Fatal("expansion changed existing list data")
	}
}
