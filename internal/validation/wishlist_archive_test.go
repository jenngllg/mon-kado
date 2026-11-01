package validation_test

import (
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func TestWishlistArchive_WhenBodyVaries_AcceptsOnlyAnExplicitBoolean(t *testing.T) {
	for _, scenario := range []struct {
		name, body string
		valid      bool
	}{
		{"archive", `{"isArchived":true}`, true},
		{"restore", `{"isArchived":false}`, true},
		{"omitted", `{}`, false},
		{"null", `{"isArchived":null}`, false},
		{"text", `{"isArchived":"true"}`, false},
		{"number", `{"isArchived":1}`, false},
		{"metadata", `{"isArchived":true,"name":"Changed"}`, false},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			var input domain.SetWishlistArchivedRequest
			// Act
			err := validation.Decode(json.NewDecoder(strings.NewReader(scenario.body)), &input)
			if err == nil {
				err = validation.Validate(input)
			}
			// Assert
			if (err == nil) != scenario.valid {
				t.Fatalf("unexpected archive validation result: %v", err)
			}
			if scenario.name == "omitted" || scenario.name == "null" {
				var result *domain.Error
				if !errors.As(err, &result) || len(result.Validation) != 1 || result.Validation[0].PropertyName != "isArchived" {
					t.Fatal("archive validation path changed")
				}
			}
		})
	}
}
