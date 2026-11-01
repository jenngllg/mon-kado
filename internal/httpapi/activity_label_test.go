package httpapi

import "testing"

func TestActivityLabel_WhenRouteContainsIdentifiers_UsesOnlySafeTemplate(t *testing.T) {
	// Arrange
	pattern := "PUT /api/v1/groups/{groupId}/wishlists/{wishlistId}/wishes/{wishId}/reservations/current"
	// Act
	result := activityLabel(pattern)
	// Assert
	if result != "Modification · groupes / listes / souhaits / réservations / actuel" {
		t.Fatalf("unexpected label: %q", result)
	}
}
