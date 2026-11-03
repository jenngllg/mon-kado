package validation

import (
	"encoding/json"
	"errors"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestValidate_WhenNotificationPreferencesAreMissing_ReportsEveryChoice(t *testing.T) {
	// Arrange / Act
	err := Validate(domain.UpdateNotificationPreferencesRequest{})
	// Assert
	var result *domain.Error
	if !errors.As(err, &result) || len(result.Validation) != 9 {
		t.Fatal("all nine missing choices must be reported")
	}
	for index, name := range []string{"wishesAdded", "groupJoined", "groupInvitation", "occasionReminder", "sharingReminder", "groupWishlistShared", "reservationUnavailable", "wishReserved", "suggestionAdded"} {
		if result.Validation[index].PropertyName != name {
			t.Fatalf("unexpected preference path %s", result.Validation[index].PropertyName)
		}
	}
}

func TestValidate_WhenNotificationPreferencesAreExplicitlyFalse_AcceptsEveryChoice(t *testing.T) {
	// Arrange
	var input domain.UpdateNotificationPreferencesRequest
	if err := json.Unmarshal([]byte(`{"wishesAdded":false,"groupJoined":false,"groupInvitation":false,"occasionReminder":false,"sharingReminder":false,"groupWishlistShared":false,"reservationUnavailable":false,"wishReserved":false,"suggestionAdded":false}`), &input); err != nil {
		t.Fatal(err)
	}
	// Act / Assert
	if err := Validate(input); err != nil {
		t.Fatal(err)
	}
}
