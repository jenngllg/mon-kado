package validation

import (
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestValidateAt_WhenEventDateChanges_UsesCurrentUTCCalendarDay(t *testing.T) {
	for _, tc := range []struct {
		date  string
		valid bool
	}{{"2026-10-10", false}, {"2026-10-11", true}, {"2026-10-12", true}, {"invalid", false}} {
		t.Run(tc.date, func(t *testing.T) {
			// Arrange
			name, occasion := "Anniversaire", "birthday"
			request := domain.CreateWishlistRequest{Name: &name, Occasion: &occasion, EventDate: &tc.date}
			now := time.Date(2026, 10, 12, 1, 0, 0, 0, time.FixedZone("UTC+2", 2*60*60))
			// Act
			err := ValidateAt(request, now)
			// Assert
			if (err == nil) != tc.valid {
				t.Fatalf("date %s: %v", tc.date, err)
			}
			var failure *domain.Error
			if err != nil && (!errors.As(err, &failure) || failure.Validation[0].PropertyName != "eventDate") {
				t.Fatal("date error lost its JSON path")
			}
		})
	}
}

func TestValidate_WhenOrderEmptyOrDuplicate_UsesUUIDIdentityAndCollectionBounds(t *testing.T) {
	id := "019a0983-abcd-7000-8000-000000000001"
	for _, tc := range []struct {
		name  string
		ids   []string
		valid bool
	}{{"empty", []string{}, true}, {"missing", nil, false}, {"one", []string{id}, true}, {"sameUUID", []string{id, strings.ToUpper(id)}, false}, {"urn", []string{"urn:uuid:" + id}, false}, {"limit", make([]string, 1001), false}} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			request := domain.ReorderWishesRequest{WishIds: tc.ids}
			// Act
			err := Validate(request)
			// Assert
			if (err == nil) != tc.valid {
				t.Fatalf("unexpected order validation: %v", err)
			}
		})
	}
}
