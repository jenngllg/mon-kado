//go:build integration

package httpapi_test

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"net/url"
	"strings"
	"testing"
	"time"
)

type googleFake struct {
	Identity application.GoogleIdentity
	Calls    int
}

func TestCurrentSession_WhenGoogleAssociationDiffers_ReturnsPersistedFlagForAnySignInMethod(t *testing.T) {
	for _, test := range []struct {
		name, email string
		linked      bool
	}{
		{"localGmail", "local-only@gmail.com", false},
		{"linkedNonGmail", "google-linked@example.test", true},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			f.member(t, test.email)
			if test.linked {
				provider := &googleFake{Identity: application.GoogleIdentity{Subject: "session-google-subject", Email: test.email}}
				challenge, err := f.service.StartGoogle(t.Context(), "https://api.example.test/api/v1/auth/google/callback", false, "")
				if err != nil {
					t.Fatal(err)
				}
				proof, err := f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider)
				if err != nil {
					t.Fatal(err)
				}
				password := "A long test password 🎁"
				session, err := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, &password)
				if err != nil {
					t.Fatal(err)
				}
				f.access = session.Access.AccessToken
			}
			// Act
			var current map[string]any
			requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &current)
			// Assert
			if linked, present := current["isGoogleLinked"].(bool); !present || linked != test.linked {
				t.Fatal("session must expose its persisted Google association as a boolean")
			}
			var localSession domain.AccessTokenResponse
			requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", jsonInput(t, map[string]string{"email": test.email, "password": "A long test password 🎁"}), nil), 200, &localSession)
			f.access = localSession.AccessToken
			var restored domain.CurrentSessionResponse
			requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &restored)
			if restored.IsGoogleLinked != test.linked {
				t.Fatal("local sign-in changed the stored Google association")
			}
		})
	}
}

func TestGoogle_WhenAssociationChangesDuringMFA_DoesNotClaimCredentialsOnConflict(t *testing.T) {
	// Arrange
	f := newFixture(t)
	other := f.member(t, "other@example.test")
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	f.service.Crypto.Now = f.service.Now
	user, key := f.administratorWithAuthenticator(t, "administrator@gmail.com")
	previous := f.access
	provider := &googleFake{Identity: application.GoogleIdentity{Subject: "conflicting-google-subject", Email: "administrator@gmail.com", DisplayName: "Claimed name"}}
	challenge, err := f.service.StartGoogle(t.Context(), "https://api.example.test/api/v1/auth/google/callback", true, "")
	if err != nil {
		t.Fatal(err)
	}
	proof, err := f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider)
	if err != nil {
		t.Fatal(err)
	}
	result, err := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, nil)
	if err != nil || result.Challenge == nil {
		t.Fatalf("expected deferred MFA: %v", err)
	}
	var originalHash, originalStamp string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT password_hash,security_stamp FROM users WHERE id=$1`, user).Scan(&originalHash, &originalStamp); err != nil {
		t.Fatal(err)
	}
	if !security.VerifyPassword("A long test password 🎁", originalHash) {
		t.Fatal("password was claimed before factor verification")
	}
	if _, err := f.store.Pool.Exec(t.Context(), `INSERT INTO user_logins(login_provider,provider_key,provider_display_name,user_id) VALUES('Google',$1,'Google',$2)`, provider.Identity.Subject, other); err != nil {
		t.Fatal(err)
	}
	now = now.Add(30 * time.Second)
	code, err := security.AuthenticatorCode(key, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	// Act
	_, err = f.service.CompleteTwoFactor(t.Context(), domain.TwoFactorCompletionRequest{Flow: &result.Challenge.Flow, Code: &code}, application.Principal{})
	// Assert
	var failure *domain.Error
	if !errors.As(err, &failure) || failure.Code != domain.GoogleAccountLinkConflict {
		t.Fatalf("expected association conflict: %v", err)
	}
	var hash, stamp string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT password_hash,security_stamp FROM users WHERE id=$1`, user).Scan(&hash, &stamp); err != nil {
		t.Fatal(err)
	}
	if hash != originalHash || stamp != originalStamp {
		t.Fatal("conflicting association partially changed credentials")
	}
	if _, err := f.service.Authenticate(t.Context(), previous); err != nil {
		t.Fatal("conflicting association revoked the original session")
	}
	if provider.Calls != 1 {
		t.Fatal("unexpected provider exchange")
	}
}

func (g *googleFake) Exchange(ctx context.Context, code, verifier, nonce, redirect string) (application.GoogleIdentity, error) {
	g.Calls++
	if ctx.Err() != nil {
		return application.GoogleIdentity{}, ctx.Err()
	}
	if code == "" || len(verifier) != 43 || len(nonce) != 43 {
		return application.GoogleIdentity{}, errors.New("invalid protocol proof")
	}
	return g.Identity, nil
}

func TestGoogle_WhenVerifiedGmailSignsIn_ClaimsAccountAndConsumesBrowserFlowOnce(t *testing.T) {
	// Arrange
	f := newFixture(t)
	user := f.member(t, "claim@gmail.com")
	previous := f.access
	provider := &googleFake{Identity: application.GoogleIdentity{Subject: "trusted-google-subject", Email: "claim@gmail.com", DisplayName: "Google member"}}
	challenge, err := f.service.StartGoogle(t.Context(), "https://api.example.test/api/v1/auth/google/callback", true, "")
	if err != nil {
		t.Fatal(err)
	}
	// Act
	if _, err = f.service.GoogleCallback(t.Context(), challenge.State, "wrong-browser", "code", provider); err == nil {
		t.Fatal("wrong browser accepted")
	}
	proof, err := f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider); err == nil {
		t.Fatal("state replay accepted")
	}
	if _, err = f.service.CompleteGoogle(t.Context(), proof, "wrong-browser", nil); err == nil {
		t.Fatal("completion browser mismatch accepted")
	}
	session, err := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, nil)
	if err != nil {
		t.Fatal(err)
	}
	principal, err := f.service.Authenticate(t.Context(), session.Access.AccessToken)
	if err != nil {
		t.Fatal(err)
	}
	_, replay := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, nil)
	_, revoked := f.service.Authenticate(t.Context(), previous)
	// Assert
	var hash *string
	if err = f.store.Pool.QueryRow(t.Context(), `SELECT password_hash FROM users WHERE id=$1`, user).Scan(&hash); err != nil {
		t.Fatal(err)
	}
	if principal.MemberID != user || !session.Persistent || hash != nil || replay == nil || revoked == nil || provider.Calls != 1 {
		t.Fatal("authoritative account claim invariant failed")
	}
}

func TestGoogle_WhenWorkspaceMatchesConfirmedLocalAccount_RequiresPasswordBeforeLinking(t *testing.T) {
	// Arrange
	f := newFixture(t)
	user := f.member(t, "member@workspace.example")
	provider := &googleFake{Identity: application.GoogleIdentity{Subject: "workspace-google-subject", Email: "member@workspace.example", HostedDomain: "workspace.example"}}
	challenge, err := f.service.StartGoogle(t.Context(), "https://api.example.test/api/v1/auth/google/callback", false, "")
	if err != nil {
		t.Fatal(err)
	}
	proof, err := f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider)
	if err != nil {
		t.Fatal(err)
	}
	// Act
	_, err = f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, nil)
	var required *domain.Error
	if !errors.As(err, &required) || required.Code != domain.GoogleAccountLinkRequired {
		t.Fatalf("unexpected result %v", err)
	}
	password := "A long test password 🎁"
	session, err := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, &password)
	if err != nil {
		t.Fatal(err)
	}
	principal, err := f.service.Authenticate(t.Context(), session.Access.AccessToken)
	// Assert
	if err != nil || principal.MemberID != user {
		t.Fatal("explicit linking failed")
	}
}

func TestGoogle_WhenIdentityIsNotAuthoritative_RequiresAdditionalVerificationWithoutCreatingAccount(t *testing.T) {
	// Arrange
	f := newFixture(t)
	provider := &googleFake{Identity: application.GoogleIdentity{Subject: "personal-google-subject", Email: "member@personal.example"}}
	challenge, err := f.service.StartGoogle(t.Context(), "https://api.example.test/api/v1/auth/google/callback", false, "")
	if err != nil {
		t.Fatal(err)
	}
	proof, err := f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider)
	if err != nil {
		t.Fatal(err)
	}
	// Act
	_, err = f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, nil)
	// Assert
	var failure *domain.Error
	if !errors.As(err, &failure) || failure.Status != 409 || failure.Code != domain.GoogleAdditionalVerificationRequired {
		t.Fatal("non-authoritative identity bypassed verification")
	}
	var count int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM users`).Scan(&count); err != nil || count != 0 {
		t.Fatal("rejected identity persisted an account")
	}
	if provider.Calls != 1 {
		t.Fatal("identity proof was exchanged more than once")
	}
}

func TestGoogle_WhenRepeatedLocalProofsFail_LocksAccountAndAllowsLinkAfterExpiry(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC().Truncate(time.Microsecond)
	f.service.Now = func() time.Time { return now }
	member := f.member(t, "locked@workspace.example")
	provider := &googleFake{Identity: application.GoogleIdentity{Subject: "locked-google-subject", Email: "locked@workspace.example", HostedDomain: "workspace.example"}}
	start := func() (application.GoogleChallenge, string) {
		t.Helper()
		challenge, err := f.service.StartGoogle(t.Context(), "https://api.example.test/api/v1/auth/google/callback", false, "")
		if err != nil {
			t.Fatal(err)
		}
		proof, err := f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider)
		if err != nil {
			t.Fatal(err)
		}
		return challenge, proof
	}
	challenge, proof := start()
	wrong, password := "Incorrect local proof", "A long test password 🎁"
	// Act
	for index := 0; index < 5; index++ {
		_, err := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, &wrong)
		var failure *domain.Error
		if !errors.As(err, &failure) || failure.Code != domain.GoogleAccountLinkFailed {
			t.Fatal("wrong local proof was accepted")
		}
	}
	_, blocked := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, &password)
	now = now.Add(6 * time.Minute)
	challenge, proof = start()
	session, err := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, &password)
	// Assert
	var failure *domain.Error
	if !errors.As(blocked, &failure) || failure.Code != domain.GoogleAuthenticationFailed || err != nil {
		t.Fatal("Google linking lockout did not expire correctly")
	}
	principal, err := f.service.Authenticate(t.Context(), session.Access.AccessToken)
	if err != nil || principal.MemberID != member {
		t.Fatal("linked session does not belong to proved member")
	}
	challenge, proof = start()
	session, err = f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, nil)
	if err != nil || session.Access.AccessToken == "" {
		t.Fatal("already linked identity still required a local password")
	}
	provider.Identity.Subject = "different-google-subject"
	challenge, proof = start()
	_, err = f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, &password)
	if !errors.As(err, &failure) || failure.Code != domain.GoogleAccountLinkConflict || provider.Calls != 4 {
		t.Fatal("conflicting Google association was accepted or re-exchanged")
	}
}

func TestGoogle_WhenClaimingPendingAccount_ConfirmsAndReplacesNameBeforeCreatingSession(t *testing.T) {
	// Arrange
	f := newFixture(t)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/registrations", `{"email":"pending-google@gmail.com","password":"A long test password 🎁","displayName":"Unconfirmed name"}`, nil), 202, nil)
	provider := &googleFake{Identity: application.GoogleIdentity{Subject: "pending-google-subject", Email: "pending-google@gmail.com"}}
	challenge, err := f.service.StartGoogle(t.Context(), "https://api.example.test/api/v1/auth/google/callback", false, "")
	if err != nil {
		t.Fatal(err)
	}
	proof, err := f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider)
	if err != nil {
		t.Fatal(err)
	}
	// Act
	session, err := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, nil)
	if err != nil {
		t.Fatal(err)
	}
	f.access = session.Access.AccessToken
	var current domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &current)
	// Assert
	if current.DisplayName != "MonKado member" || current.Email != provider.Identity.Email {
		t.Fatal("pending account was not replaced by the verified identity")
	}
	var hash *string
	var confirmed bool
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT password_hash,email_confirmed FROM users WHERE email=$1`, provider.Identity.Email).Scan(&hash, &confirmed); err != nil || hash != nil || !confirmed {
		t.Fatal("pending local credential survived the authoritative claim")
	}
}

func TestGoogle_WhenReplacingExistingBrowserSession_RevokesPreviousRefreshSession(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "previous@gmail.com")
	previous := f.access
	u, err := url.Parse(f.server.URL)
	if err != nil {
		t.Fatal(err)
	}
	var refresh string
	for _, cookie := range f.client.Jar.Cookies(u) {
		if strings.HasSuffix(cookie.Name, ".Refresh") {
			refresh = cookie.Value
		}
	}
	if refresh == "" {
		t.Fatal("previous refresh cookie missing")
	}
	provider := &googleFake{Identity: application.GoogleIdentity{Subject: "previous-google-subject", Email: "previous@gmail.com"}}
	challenge, err := f.service.StartGoogle(t.Context(), "https://api.example.test/api/v1/auth/google/callback", true, refresh)
	if err != nil {
		t.Fatal(err)
	}
	proof, err := f.service.GoogleCallback(t.Context(), challenge.State, challenge.Binding, "code", provider)
	if err != nil {
		t.Fatal(err)
	}
	// Act
	session, err := f.service.CompleteGoogle(t.Context(), proof, challenge.Binding, nil)
	// Assert
	if err != nil || !session.Persistent {
		t.Fatal("replacement browser session failed")
	}
	if _, err := f.service.Authenticate(t.Context(), previous); err == nil {
		t.Fatal("previous access session survived Google replacement")
	}
	if _, err := f.service.Refresh(t.Context(), refresh); err == nil {
		t.Fatal("previous browser refresh survived Google replacement")
	}
	if provider.Calls != 1 {
		t.Fatal("Google replacement exchanged more than once")
	}
}
