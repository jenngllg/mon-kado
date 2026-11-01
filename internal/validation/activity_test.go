package validation_test

import (
	"errors"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func TestActivityPage_WhenIdentifierIsApproved_ReturnsSafeLabel(t *testing.T) {
	// Arrange
	cases := []struct {
		input, expected string
		valid           bool
	}{{"group-wishlist", "Liste du groupe", true}, {"list-details", "Détail d’une liste", true}, {"/lists/id?token=secret", "", false}, {"", "", false}}
	for _, tc := range cases {
		t.Run(tc.input, func(t *testing.T) {
			// Act
			label, err := validation.ActivityPage(tc.input)
			// Assert
			if (err == nil) != tc.valid || label != tc.expected {
				t.Fatalf("label %q, error %v", label, err)
			}
		})
	}
}

func TestActivityQuery_WhenFiltersAreInvalid_AggregatesErrors(t *testing.T) {
	// Arrange
	values := url.Values{"q": {"a", "b"}, "kind": {"other"}, "logLevel": {"debug"}}
	// Act
	_, _, _, err := validation.ActivityQuery(values)
	// Assert
	var failure *domain.Error
	if !errors.As(err, &failure) || len(failure.Validation) != 3 {
		t.Fatalf("invalid filters not aggregated: %v", err)
	}
}

func TestActivityQuery_WhenSeverityIsValid_UsesCanonicalLevel(t *testing.T) {
	for _, level := range []string{"info", "error", "ERROR"} {
		t.Run(level, func(t *testing.T) {
			// Arrange / Act
			_, _, actual, err := validation.ActivityQuery(url.Values{"logLevel": {level}})
			// Assert
			if err != nil || actual != "info" && actual != "error" {
				t.Fatalf("invalid canonical severity: %q %v", actual, err)
			}
		})
	}
}
