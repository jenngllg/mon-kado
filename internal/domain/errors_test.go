package domain_test

import (
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestFailure_WhenFormatted_RetainsStableCodeAndSafeMessage(t *testing.T) {
	// Arrange
	code, message := domain.WishlistNotFound, "The requested resource was not found."
	// Act
	err := domain.Failure(404, code, message)
	// Assert
	if err.Status != 404 || err.Code != code || err.Message != message || err.Error() != code+": "+message {
		t.Fatal("business failure contract differs")
	}
}
