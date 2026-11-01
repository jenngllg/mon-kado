//go:build integration

package httpapi_test

import (
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestTwoFactorManagement_WhenRecoveryCodesRegenerated_BindsGrantToOriginalSessionAndRevokesSessions(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	f.service.Crypto.Now = f.service.Now
	member, key := f.administratorWithAuthenticator(t, "factor@example.test")
	first := f.access
	principal, err := f.service.Authenticate(t.Context(), first)
	if err != nil {
		t.Fatal(err)
	}
	now = now.Add(30 * time.Second)
	login, err := f.service.Login(t.Context(), domain.LoginRequest{Email: stringPointer("factor@example.test"), Password: stringPointer("A long test password 🎁")}, "")
	if err != nil || login.Challenge == nil {
		t.Fatal("second browser factor challenge missing")
	}
	code, err := security.AuthenticatorCode(key, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	second, err := f.service.CompleteTwoFactor(t.Context(), domain.TwoFactorCompletionRequest{Flow: &login.Challenge.Flow, Code: &code}, application.Principal{})
	if err != nil {
		t.Fatal(err)
	}
	secondPrincipal, err := f.service.Authenticate(t.Context(), second.Access.AccessToken)
	if err != nil {
		t.Fatal(err)
	}
	now = now.Add(30 * time.Second)
	code, err = security.AuthenticatorCode(key, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	var grant domain.TwoFactorChallengeResponse
	// Act
	requireStatus(t, f.send(t, "POST", "/api/v1/members/current/two-factor/reauthentications", jsonInput(t, map[string]string{"purpose": "regenerateRecoveryCodes", "code": code}), nil), 200, &grant)
	if _, err := f.service.RegenerateRecoveryCodes(t.Context(), grant.Flow, secondPrincipal); err == nil {
		t.Fatal("another browser used the management grant")
	}
	if _, err := f.service.TwoFactorSetup(t.Context(), grant.Flow, principal); err == nil {
		t.Fatal("regeneration grant installed an authenticator")
	}
	var result domain.TwoFactorRecoveryCodesResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/recovery-codes/regenerations", jsonInput(t, map[string]string{"flow": grant.Flow}), nil), 200, &result)
	// Assert
	if grant.RequiredAction != "complete" || len(result.RecoveryCodes) != 10 {
		t.Fatal("management grant or recovery code contract differs")
	}
	for _, access := range []string{first, second.Access.AccessToken} {
		if _, err := f.service.Authenticate(t.Context(), access); err == nil {
			t.Fatal("regeneration left a prior session valid")
		}
	}
	if _, err := f.service.RegenerateRecoveryCodes(t.Context(), grant.Flow, principal); err == nil {
		t.Fatal("management grant was reusable")
	}
	var hashed, plaintext int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FILTER(WHERE code_hash=$2),count(*) FILTER(WHERE code_hash=$3) FROM two_factor_recovery_codes WHERE member_id=$1`, member, security.HashToken(security.NormalizeRecoveryCode(result.RecoveryCodes[0])), []byte(result.RecoveryCodes[0])).Scan(&hashed, &plaintext); err != nil {
		t.Fatal(err)
	}
	if hashed != 1 || plaintext != 0 {
		t.Fatal("recovery code hashing invariant violated")
	}
}

func TestTwoFactorManagement_WhenAuthenticatorReplaced_ConsumesGrantAndInvalidatesOriginalSession(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	f.service.Crypto.Now = f.service.Now
	_, key := f.administratorWithAuthenticator(t, "replace@example.test")
	principal, err := f.service.Authenticate(t.Context(), f.access)
	if err != nil {
		t.Fatal(err)
	}
	now = now.Add(30 * time.Second)
	code, err := security.AuthenticatorCode(key, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	grant, err := f.service.ReauthenticateTwoFactor(t.Context(), principal, domain.TwoFactorReauthenticationRequest{Purpose: stringPointer("replaceAuthenticator"), Code: &code})
	if err != nil {
		t.Fatal(err)
	}
	// Act
	setup, err := f.service.TwoFactorSetup(t.Context(), grant.Flow, principal)
	if err != nil {
		t.Fatal(err)
	}
	repeated, err := f.service.TwoFactorSetup(t.Context(), grant.Flow, principal)
	if err != nil || repeated.ManualKey != setup.ManualKey {
		t.Fatal("a repeated setup request changed its candidate")
	}
	code, err = security.AuthenticatorCode(setup.ManualKey, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	codes, err := f.service.ConfirmTwoFactorSetup(t.Context(), grant.Flow, code, principal)
	if err != nil {
		t.Fatal(err)
	}
	// Assert
	if grant.RequiredAction != "replace" || setup.ManualKey == key || len(codes.RecoveryCodes) != 10 {
		t.Fatal("authenticator was not replaced")
	}
	if _, err := f.service.Authenticate(t.Context(), f.access); err == nil {
		t.Fatal("replacement kept the old session")
	}
	if _, err := f.service.ConfirmTwoFactorSetup(t.Context(), grant.Flow, code, principal); err == nil {
		t.Fatal("replacement proof was replayed")
	}
}

func TestTwoFactor_WhenRepeatedProofsFail_LocksFactorAndRejectsExpiredFlows(t *testing.T) {
	// Arrange
	f := newFixture(t)
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	f.service.Crypto.Now = f.service.Now
	_, key := f.administratorWithAuthenticator(t, "lockedfactor@example.test")
	f.access = ""
	f.csrfToken(t)
	var grant domain.TwoFactorChallengeResponse
	login := `{"email":"lockedfactor@example.test","password":"A long test password 🎁"}`
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", login, nil), 202, &grant)
	wrong := "000000"
	now = now.Add(30 * time.Second)
	valid, err := security.AuthenticatorCode(key, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	if valid == wrong {
		wrong = "111111"
	}
	// Act
	for attempt := 0; attempt < 5; attempt++ {
		requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": grant.Flow, "code": wrong}), nil), 401, nil)
	}
	// Assert
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": grant.Flow, "code": valid}), nil), 429, nil)
	now = now.Add(16 * time.Minute)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": grant.Flow, "code": valid}), nil), 401, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", login, nil), 202, &grant)
	valid, err = security.AuthenticatorCode(key, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": grant.Flow, "code": valid}), nil), 200, nil)
}

func stringPointer(value string) *string { return &value }
