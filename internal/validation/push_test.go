package validation_test

import (
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
	"testing"
)

func TestPushValidation_WhenInvalid_AggregatesDeviceAndNestedKindErrors(t *testing.T) {
	// Arrange / Act
	err := validation.Validate(domain.UpdatePushSubscriptionRequest{Name: "", DisabledKinds: []string{"unknown", "wishesAdded", "wishesAdded"}})
	// Assert
	var failure *domain.Error
	if !errors.As(err, &failure) || len(failure.Validation) != 3 {
		t.Fatalf("device errors not aggregated: %v", err)
	}
	if err := validation.Validate(domain.UpdatePushSubscriptionRequest{Name: "Phone", DisabledKinds: []string{}}); err != nil {
		t.Fatal("all-enabled device rejected", err)
	}
}
