//go:build integration

package httpapi_test

import (
	"crypto/pbkdf2"
	"crypto/sha512"
	"encoding/base64"
	"encoding/binary"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/security"
)

func TestLogin_WhenPasswordPolicyIsOutdated_UpgradesOnlyConfirmedSuccessfulCredentials(t *testing.T) {
	for _, tc := range []struct {
		name                string
		iterations, status  int
		wrong, hidden, lock bool
		upgrade             bool
	}{
		{"previousPolicy", 100000, 200, false, false, false, true},
		{"currentPolicy", 220000, 200, false, false, false, false},
		{"higherCost", 300000, 200, false, false, false, false},
		{"wrongPassword", 100000, 401, true, false, false, false},
		{"unconfirmed", 100000, 403, false, true, false, false},
		{"locked", 100000, 401, false, false, true, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			id := f.member(t, "password-upgrade@example.test")
			password := "A long test password 🎁"
			stored := legacyLoginPassword(t, password, tc.iterations)
			var lockout *time.Time
			if tc.lock {
				until := f.service.Now().UTC().Add(time.Hour)
				lockout = &until
			}
			if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET password_hash=$2,email_confirmed=$3,lockout_end=$4 WHERE id=$1`, id, stored, !tc.hidden, lockout); err != nil {
				t.Fatal(err)
			}
			f.access = ""
			f.csrfToken(t)
			if tc.wrong {
				password = "A different test password"
			}
			// Act
			requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", jsonInput(t, map[string]any{"email": "password-upgrade@example.test", "password": password}), nil), tc.status, nil)
			// Assert: persistence access verifies the non-public credential invariant.
			var current string
			if err := f.store.Pool.QueryRow(t.Context(), `SELECT password_hash FROM users WHERE id=$1`, id).Scan(&current); err != nil {
				t.Fatal(err)
			}
			if (current != stored) != tc.upgrade || !security.VerifyPassword("A long test password 🎁", current) {
				t.Fatal("login changed an unproven credential or failed to preserve its password")
			}
			if tc.upgrade && security.PasswordNeedsRehash(current) {
				t.Fatal("successful login retained an outdated password hash")
			}
		})
	}
}

func legacyLoginPassword(t *testing.T, password string, iterations int) string {
	t.Helper()
	salt := []byte("migration-salt!!")
	key, err := pbkdf2.Key(sha512.New, password, salt, iterations, 32)
	if err != nil {
		t.Fatal(err)
	}
	data := make([]byte, 13)
	data[0] = 1
	binary.BigEndian.PutUint32(data[1:5], 2)
	binary.BigEndian.PutUint32(data[5:9], uint32(iterations))
	binary.BigEndian.PutUint32(data[9:13], uint32(len(salt)))
	return base64.StdEncoding.EncodeToString(append(append(data, salt...), key...))
}
