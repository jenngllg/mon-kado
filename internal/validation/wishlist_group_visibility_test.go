package validation_test

import (
	"errors"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func TestValidate_WhenGroupVisibilityChoicesInvalid_AggregatesNestedPaths(t *testing.T) {
	id := "019c52dd-56c1-7cc6-8a95-243f3a032e04"
	visible := true
	for _, test := range []struct {
		name   string
		groups *[]domain.WishlistGroupVisibilitySelection
		paths  []string
	}{
		{"missing", nil, []string{"groups"}},
		{"empty", &[]domain.WishlistGroupVisibilitySelection{}, nil},
		{"missingFields", &[]domain.WishlistGroupVisibilitySelection{{}}, []string{"groups[0].groupId", "groups[0].isVisible"}},
		{"duplicate", &[]domain.WishlistGroupVisibilitySelection{{GroupId: &id, IsVisible: &visible}, {GroupId: &id, IsVisible: &visible}}, []string{"groups[1].groupId"}},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange / Act
			err := validation.Validate(domain.SetWishlistGroupVisibilityRequest{Groups: test.groups})
			// Assert
			if test.paths == nil {
				if err != nil {
					t.Fatal(err)
				}
				return
			}
			var failure *domain.Error
			if !errors.As(err, &failure) || len(failure.Validation) != len(test.paths) {
				t.Fatalf("unexpected validation: %v", err)
			}
			for index, path := range test.paths {
				if failure.Validation[index].PropertyName != path {
					t.Fatalf("missing path %s", path)
				}
			}
		})
	}
}
