//go:build integration

package httpapi_test

import (
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestEmailConfirmationRequest_WhenCSRFCookieRotated_CanRefreshAndRetry(t *testing.T) {
	// Arrange: another tab rotates the cookie while this tab retains its old token.
	f := newFixture(t)
	oldToken := f.csrf
	f.csrfToken(t)
	newToken := f.csrf
	f.csrf = oldToken
	const path = "/api/v1/auth/email-confirmation-requests"
	const body = `{"email":"csrf-retry@example.test"}`
	// Act: the outdated request is rejected before the operation is executed.
	var failure domain.ErrorResponse
	requireStatus(t, f.send(t, "POST", path, body, nil), 400, &failure)
	// Assert: clients can distinguish a safe CSRF retry from malformed input.
	if failure.ErrorCode == nil || *failure.ErrorCode != domain.SecurityCSRFValidationFailed {
		t.Fatal("rotated CSRF cookie did not return the retryable security code")
	}
	f.csrf = newToken
	requireStatus(t, f.send(t, "POST", path, body, nil), 202, nil)
}
