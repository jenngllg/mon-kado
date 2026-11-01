//go:build integration

package persistence_test

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/persistence"
	"github.com/JennGllg/mon-kado/internal/security"
)

func administratorLegacyStore(t *testing.T) *persistence.Store {
	t.Helper()
	store := migrationStore(t)
	if _, err := store.Pool.Exec(t.Context(), `CREATE TABLE mon_kado_migrations(name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())`); err != nil {
		t.Fatal(err)
	}
	files, err := os.ReadDir("migrations")
	if err != nil {
		t.Fatal(err)
	}
	for _, file := range files {
		if file.Name() >= "020_" {
			break
		}
		sql, err := os.ReadFile(filepath.Join("migrations", file.Name()))
		if err != nil {
			t.Fatal(err)
		}
		if _, err = store.Pool.Exec(t.Context(), string(sql)); err != nil {
			t.Fatal(err)
		}
		if _, err = store.Pool.Exec(t.Context(), `INSERT INTO mon_kado_migrations(name) VALUES($1)`, file.Name()); err != nil {
			t.Fatal(err)
		}
	}
	return store
}

func TestMigrate_WhenAdministratorRolesExist_PreservesPrivilegesAsBooleans(t *testing.T) {
	// Arrange
	store := administratorLegacyStore(t)
	for _, role := range []string{"MEMBER", "ADMIN", "ADMINISTRATOR"} {
		id := security.NewID()
		if _, err := store.Pool.Exec(t.Context(), `INSERT INTO users(id,display_name,created_at,updated_at,user_name,normalized_user_name,email,normalized_email,email_confirmed,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) VALUES($1,$2,now(),now(),$2,$2,$2,$2,true,false,false,true,0)`, id, role); err != nil {
			t.Fatal(err)
		}
		if _, err := store.Pool.Exec(t.Context(), `INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE normalized_name=$2`, id, role); err != nil {
			t.Fatal(err)
		}
	}
	// Act
	if err := store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err := store.Migrate(t.Context()); err != nil {
		t.Fatal(err)
	}
	// Assert
	for _, role := range []string{"MEMBER", "ADMIN", "ADMINISTRATOR"} {
		var administrator bool
		if err := store.Pool.QueryRow(t.Context(), `SELECT is_admin FROM users WHERE display_name=$1`, role).Scan(&administrator); err != nil || administrator != (role != "MEMBER") {
			t.Fatal("migration changed existing administrator privileges")
		}
	}
	var removed bool
	if err := store.Pool.QueryRow(t.Context(), `SELECT to_regclass('roles') IS NULL AND to_regclass('user_roles') IS NULL AND to_regclass('role_claims') IS NULL`).Scan(&removed); err != nil || !removed {
		t.Fatal("textual role persistence remains")
	}
}

func TestMigrate_WhenUnrepresentablePrivilegesExist_RejectsBeforeChangingAccess(t *testing.T) {
	for _, scenario := range []string{"unknownRole", "roleClaim"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			store := administratorLegacyStore(t)
			sql := `INSERT INTO roles(id,name,normalized_name) VALUES('0198d027-51c0-7000-8000-000000000099','Operator','OPERATOR')`
			if scenario == "roleClaim" {
				sql = `INSERT INTO role_claims(role_id,claim_type,claim_value) SELECT id,'permission','custom' FROM roles LIMIT 1`
			}
			if _, err := store.Pool.Exec(t.Context(), sql); err != nil {
				t.Fatal(err)
			}
			// Act
			err := store.Migrate(t.Context())
			// Assert
			if err == nil || !strings.Contains(err.Error(), "020_administrator_boolean.sql") {
				t.Fatal("unrepresentable privileges were discarded")
			}
			var untouched bool
			if err := store.Pool.QueryRow(t.Context(), `SELECT to_regclass('roles') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='is_admin')`).Scan(&untouched); err != nil || !untouched {
				t.Fatal("rejected migration changed access persistence")
			}
		})
	}
}
