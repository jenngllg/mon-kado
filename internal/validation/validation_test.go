package validation

import (
	"encoding/json"
	"errors"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestValidate_WhenMultiplePropertiesAreInvalid_AggregatesPaths(t *testing.T) {
	// Arrange
	request := domain.RegisterAccountRequest{}
	// Act
	err := Validate(request)
	// Assert
	var result *domain.Error
	if !errors.As(err, &result) || len(result.Validation) != 3 {
		t.Fatalf("expected three validation errors, got %v", err)
	}
	for i, name := range []string{"email", "password", "displayName"} {
		if result.Validation[i].PropertyName != name {
			t.Fatalf("expected path %s", name)
		}
	}
}

func TestValidate_WhenPriceHasInvalidPrecision_RejectsWithoutRounding(t *testing.T) {
	for _, tc := range []struct {
		value string
		valid bool
	}{{"1.23", true}, {"0.01", true}, {"99999999.99", true}, {"1.234", false}, {"0.00", false}, {"100000000.00", false}, {"1e4", false}} {
		// Arrange
		name, price := "Gift", json.Number(tc.value)
		request := domain.CreateWishRequest{Name: &name, Price: &price}
		// Act
		err := Validate(request)
		// Assert
		if (err == nil) != tc.valid {
			t.Fatalf("price %s: %v", tc.value, err)
		}
	}
}

func TestText_WhenUnicodeOrControlsArePresent_RespectsScalarLimits(t *testing.T) {
	for _, tc := range []struct {
		text                string
		multiline, expected bool
	}{{"🎁é", false, true}, {"a\nb", false, false}, {"a\nb", true, true}, {"a\x00b", true, false}, {"a\u2028b", true, true}, {"a\u2028b", false, false}, {"abcd", true, false}} {
		// Arrange
		// Act
		result := Text(tc.text, 3, tc.multiline)
		// Assert
		if result != tc.expected {
			t.Fatalf("Text(%q) = %v", tc.text, result)
		}
	}
}
