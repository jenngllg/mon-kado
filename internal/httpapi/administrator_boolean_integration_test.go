//go:build integration

package httpapi_test

import (
	"encoding/json"
	"testing"
)

func TestCurrentSession_WhenAdministratorFlagChanges_UsesLiveBooleanWithoutRoles(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.administrator(t, "boolean-access@example.test")
	for _, isAdmin := range []bool{true, false, true} {
		if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET is_admin=$2 WHERE id=$1`, member, isAdmin); err != nil {
			t.Fatal(err)
		}
		// Act
		var identity map[string]json.RawMessage
		requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &identity)
		// Assert: the same verified session observes changes without new credentials.
		var actual bool
		if json.Unmarshal(identity["isAdmin"], &actual) != nil || actual != isAdmin || identity["roles"] != nil {
			t.Fatal("session must expose only the live administrator boolean")
		}
		expectedStatus := 403
		if isAdmin {
			expectedStatus = 200
		}
		requireStatus(t, f.send(t, "GET", "/api/v1/admin/reported-wishlists", "", nil), expectedStatus, nil)
	}
}
