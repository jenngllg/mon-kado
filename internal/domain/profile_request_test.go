package domain_test

import (
	"encoding/json"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func TestProfileRequest_WhenVisibilityIsSupplied_DistinguishesNullFromOmission(t *testing.T) {
	for _, tc := range []struct {
		name, data     string
		present, valid bool
	}{
		{"omitted", `{"displayName":"Member"}`, false, true},
		{"false", `{"displayName":"Member","isVisibleInMemberSearch":false}`, true, true},
		{"null", `{"displayName":"Member","isVisibleInMemberSearch":null}`, true, false},
		{"caseInsensitiveNull", `{"displayName":"Member","IsVisibleInMemberSearch":null}`, true, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			var request domain.UpdateMemberProfileRequest
			// Act
			err := json.Unmarshal([]byte(tc.data), &request)
			// Assert
			if err != nil || request.HasVisibilityPreference != tc.present || (validation.Validate(request) == nil) != tc.valid {
				t.Fatal("visibility presence contract differs")
			}
		})
	}
}
