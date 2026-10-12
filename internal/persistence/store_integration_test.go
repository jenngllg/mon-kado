//go:build integration

package persistence_test

import (
	"context"
	"errors"
	"net/url"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/persistence"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

func migrationStore(t *testing.T) *persistence.Store {
	t.Helper()
	connection := os.Getenv("MONKADO_TEST_DATABASE_URL")
	if connection == "" {
		t.Skip("MONKADO_TEST_DATABASE_URL must point to an isolated native PostgreSQL database")
	}
	u, err := url.Parse(connection)
	if err != nil || !strings.HasSuffix(u.Path, "_test") {
		t.Fatal("migration tests require a test database")
	}
	admin, err := persistence.Open(t.Context(), connection)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(admin.Close)
	name := "mon_kado_migration_" + strings.ReplaceAll(security.NewID(), "-", "") + "_test"
	quoted := pgx.Identifier{name}.Sanitize()
	if _, err = admin.Pool.Exec(t.Context(), "CREATE DATABASE "+quoted); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.WithoutCancel(t.Context()), 30*time.Second)
		defer cancel()
		if _, err := admin.Pool.Exec(ctx, "DROP DATABASE "+quoted+" WITH (FORCE)"); err != nil {
			t.Errorf("remove owned migration database: %v", err)
		}
	})
	u.Path = "/" + name
	store, err := persistence.Open(t.Context(), u.String())
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(store.Close)
	return store
}

func TestMigrate_WhenDatabaseEmpty_AppliesAllMigrationsExactlyOnce(t *testing.T) {
	// Arrange
	store := migrationStore(t)
	// Act
	if err := store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err := store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}
	// Assert
	var count int
	if err := store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM mon_kado_migrations`).Scan(&count); err != nil || count != 12 {
		t.Fatal("native migration ledger differs")
	}
	var leaseColumn bool
	if err := store.Pool.QueryRow(t.Context(), `SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='gift_image_deletion_outbox' AND column_name='lease_id')`).Scan(&leaseColumn); err != nil || !leaseColumn {
		t.Fatal("final migration was not applied")
	}
	var visibilityColumn bool
	if err := store.Pool.QueryRow(t.Context(), `SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='is_visible_in_member_search' AND is_nullable='NO' AND column_default='false')`).Scan(&visibilityColumn); err != nil || !visibilityColumn {
		t.Fatal("new accounts must be hidden from search until they opt in")
	}
	var timestamp time.Time
	if err := store.Pool.QueryRow(t.Context(), `SELECT '2026-10-12 14:30:00+02'::timestamptz`).Scan(&timestamp); err != nil || timestamp.Location() != time.UTC || timestamp.Format(time.RFC3339) != "2026-10-12T12:30:00Z" {
		t.Fatal("timestamp decoding must preserve the instant in UTC regardless of host location")
	}
}

func TestMigrate_WhenSchemaConflicts_RollsBackFailedMigrationAndAllowsRepair(t *testing.T) {
	// Arrange
	store := migrationStore(t)
	if _, err := store.Pool.Exec(t.Context(), `CREATE TABLE users(id text)`); err != nil {
		t.Fatal(err)
	}
	// Act
	err := store.Migrate(t.Context())
	// Assert
	if err == nil || !strings.Contains(err.Error(), "001_baseline.sql") {
		t.Fatal("invalid baseline did not fail with its migration name")
	}
	var count int
	if err := store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM mon_kado_migrations`).Scan(&count); err != nil || count != 0 {
		t.Fatal("failed migration was marked as applied")
	}
	var rolesExist bool
	if err := store.Pool.QueryRow(t.Context(), `SELECT to_regclass('public.roles') IS NOT NULL`).Scan(&rolesExist); err != nil || rolesExist {
		t.Fatal("failed baseline left a partially migrated schema")
	}
	if _, err := store.Pool.Exec(t.Context(), `DROP TABLE users`); err != nil {
		t.Fatal(err)
	}
	if err := store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}
}

func TestTransaction_WhenOperationFails_RollsBackWithoutReplacingError(t *testing.T) {
	// Arrange
	store := migrationStore(t)
	if _, err := store.Pool.Exec(t.Context(), `CREATE TABLE rollback_probe(id integer PRIMARY KEY)`); err != nil {
		t.Fatal(err)
	}
	expected := errors.New("operation rejected")
	// Act
	err := store.Transaction(t.Context(), func(tx pgx.Tx) error {
		if _, err := tx.Exec(t.Context(), `INSERT INTO rollback_probe VALUES(1)`); err != nil {
			return err
		}
		return expected
	})
	// Assert
	if !errors.Is(err, expected) {
		t.Fatal("transaction substituted the operation failure")
	}
	var count int
	if err := store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM rollback_probe`).Scan(&count); err != nil || count != 0 {
		t.Fatal("failed transaction persisted changes")
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	if err := store.Migrate(ctx); !errors.Is(err, context.Canceled) {
		t.Fatal("migration did not honor caller cancellation")
	}
}
