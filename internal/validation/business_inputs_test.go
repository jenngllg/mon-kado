package validation_test

import (
	"errors"
	"slices"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func inputPointer[T any](value T) *T { return &value }

func TestValidateAt_WhenBusinessInputsViolateLimits_AggregatesExactClientPaths(t *testing.T) {
	for _, tc := range []struct {
		name  string
		input any
		paths []string
	}{
		{"registration", &domain.RegisterAccountRequest{Email: inputPointer("not-an-email"), Password: inputPointer("short"), DisplayName: inputPointer(strings.Repeat("é", 81))}, []string{"email", "password", "displayName"}},
		{"longPassword", domain.LoginRequest{Email: inputPointer("fixture@example.test"), Password: inputPointer(strings.Repeat("a", 129))}, []string{"password"}},
		{"wish", domain.CreateWishRequest{Name: inputPointer(strings.Repeat("🎁", 101)), Note: inputPointer(strings.Repeat("a", 501)), Url: inputPointer("https://secret@merchant.test/item"), Quantity: inputPointer(101)}, []string{"name", "note", "url", "quantity"}},
		{"wishlist", domain.CreateWishlistRequest{Name: inputPointer("List"), Occasion: inputPointer("unknown"), Message: inputPointer(strings.Repeat("a", 501))}, []string{"occasion", "message"}},
		{"report", domain.ReportSharedWishlistRequest{Reason: inputPointer("unknown"), Details: inputPointer(strings.Repeat("a", 1001))}, []string{"reason", "details"}},
		{"otherReport", domain.ReportSharedWishlistRequest{Reason: inputPointer("other")}, []string{"details"}},
		{"review", domain.UpdateWishlistReportReviewRequest{Status: inputPointer("unknown"), ReviewNote: inputPointer(strings.Repeat("a", 1001))}, []string{"status", "reviewNote"}},
		{"administration", domain.ExecuteAdministrativeAccountErasureRequest{RequestReference: inputPointer(strings.Repeat("a", 129)), ConfirmedMemberId: inputPointer("00000000-0000-0000-0000-000000000000")}, []string{"requestReference", "confirmedMemberId"}},
		{"factor", domain.TwoFactorCompletionRequest{Flow: inputPointer("invalid"), Code: inputPointer("１２３４５６"), RecoveryCode: inputPointer("not-a-code")}, []string{"flow", "code", "recoveryCode", "code"}},
		{"managementPurpose", domain.TwoFactorReauthenticationRequest{Purpose: inputPointer("unknown"), Code: inputPointer("123456")}, []string{"purpose"}},
		{"managementProofMissing", domain.TwoFactorReauthenticationRequest{Purpose: inputPointer("replaceAuthenticator")}, []string{"code"}},
		{"suspensionReasonMissing", domain.UpdateWishlistModerationRequest{IsSuspended: inputPointer(true)}, []string{"reason"}},
		{"suspensionReasonTooLong", domain.UpdateWishlistModerationRequest{IsSuspended: inputPointer(true), Reason: inputPointer(strings.Repeat("a", 1001))}, []string{"reason"}},
		{"reactivationReason", domain.UpdateWishlistModerationRequest{IsSuspended: inputPointer(false), Reason: inputPointer("must be absent")}, []string{"reason"}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			now := time.Date(2026, 10, 12, 0, 0, 0, 0, time.UTC)
			// Act
			err := validation.ValidateAt(tc.input, now)
			// Assert
			var failure *domain.Error
			if !errors.As(err, &failure) || failure.Status != 400 || failure.Code != domain.RequestValidationError {
				t.Fatalf("validation error = %v", err)
			}
			paths := make([]string, 0, len(failure.Validation))
			for _, item := range failure.Validation {
				paths = append(paths, item.PropertyName)
				if item.ErrorMessage == "" {
					t.Fatal("validation message absent")
				}
			}
			if !slices.Equal(paths, tc.paths) {
				t.Fatalf("paths = %v, expected %v", paths, tc.paths)
			}
		})
	}
}
