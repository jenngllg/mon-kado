package validation

import (
	"errors"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestValidate_WhenWishVariantsProvided_EnforcesOptionalSingleLineLimit(t *testing.T) {
	for _, tc := range []struct {
		name, value string
		valid       bool
	}{
		{"empty", "", true}, {"blank", "   ", true},
		{"colour", "vert sauge", true}, {"dimensions", "140 × 200 cm", true},
		{"unicodeLimit", strings.Repeat("é", 100), true},
		{"tooLong", strings.Repeat("é", 101), false},
		{"newline", "M\nL", false}, {"control", "M\x00L", false},
		{"lineSeparator", "M\u2028L", false}, {"paragraphSeparator", "M\u2029L", false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			name := "Product"
			requests := []any{
				domain.CreateWishRequest{Name: &name, Color: &tc.value, Size: &tc.value},
				domain.UpdateWishRequest{Name: &name, Color: &tc.value, Size: &tc.value},
			}
			for _, request := range requests {
				// Act
				err := Validate(request)
				// Assert
				if tc.valid {
					if err != nil {
						t.Fatal("valid variants were rejected", err)
					}
					continue
				}
				var failure *domain.Error
				if !errors.As(err, &failure) || len(failure.Validation) != 2 || failure.Validation[0].PropertyName != "color" || failure.Validation[1].PropertyName != "size" {
					t.Fatal("invalid variants must aggregate both JSON paths")
				}
			}
		})
	}
}
