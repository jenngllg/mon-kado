package application_test

import (
	"errors"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestAuthorizeAdministrator_WhenBooleanOrMFAChanges_RequiresBoth(t *testing.T) {
	verified := time.Date(2026, time.November, 1, 12, 0, 0, 0, time.UTC)
	for _, scenario := range []struct {
		name    string
		isAdmin bool
		mfa     *time.Time
		code    string
	}{
		{"member", false, nil, domain.SecurityForbidden},
		{"memberWithMFA", false, &verified, domain.SecurityForbidden},
		{"administratorWithoutMFA", true, nil, domain.AccountTwoFactorAccessDenied},
		{"administratorWithMFA", true, &verified, ""},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange / Act
			err := application.AuthorizeAdministrator(application.Principal{IsAdmin: scenario.isAdmin, TwoFactorVerifiedAt: scenario.mfa})
			// Assert
			if scenario.code == "" {
				if err != nil {
					t.Fatal(err)
				}
				return
			}
			var failure *domain.Error
			if !errors.As(err, &failure) || failure.Status != 403 || failure.Code != scenario.code {
				t.Fatalf("unexpected access decision: %v", err)
			}
		})
	}
}
