//go:build integration

package httpapi_test

import (
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"testing"
	"time"
)

func jsonInput(t *testing.T, value any) string {
	t.Helper()
	data, err := json.Marshal(value)
	if err != nil {
		t.Fatal(err)
	}
	return string(data)
}

func TestTwoFactor_WhenAdministratorEnrollsAndRecovers_RequiresReplacementBeforeIssuingSession(t *testing.T) {
	// Arrange
	f := newFixture(t)
	user := f.member(t, "administrator@example.test")
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	f.service.Crypto.Now = f.service.Now
	if _, err := f.store.Pool.Exec(t.Context(), `INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE normalized_name='ADMINISTRATOR'`, user); err != nil {
		t.Fatal(err)
	}
	f.access = ""
	f.csrfToken(t)
	login := `{"email":"administrator@example.test","password":"A long test password 🎁"}`
	var challenge domain.TwoFactorChallengeResponse
	// Act
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", login, nil), 202, &challenge)
	var setup domain.TwoFactorSetupResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/setup", jsonInput(t, map[string]string{"flow": challenge.Flow}), nil), 200, &setup)
	code, err := security.AuthenticatorCode(setup.ManualKey, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	var codes domain.TwoFactorRecoveryCodesResponse
	confirm := jsonInput(t, map[string]string{"flow": challenge.Flow, "code": code})
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/setup/confirmations", confirm, nil), 200, &codes)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/setup/confirmations", confirm, nil), 409, nil)
	var tokens domain.AccessTokenResponse
	complete := jsonInput(t, map[string]string{"flow": challenge.Flow})
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", complete, nil), 200, &tokens)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", complete, nil), 401, nil)
	f.access = tokens.AccessToken
	var status domain.TwoFactorStatusResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/two-factor", "", nil), 200, &status)
	f.access = ""
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", login, nil), 202, &challenge)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": challenge.Flow, "code": code}), nil), 401, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": challenge.Flow, "recoveryCode": codes.RecoveryCodes[0]}), nil), 202, &challenge)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": challenge.Flow}), nil), 409, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/setup", jsonInput(t, map[string]string{"flow": challenge.Flow}), nil), 200, &setup)
	code, err = security.AuthenticatorCode(setup.ManualKey, now.Unix()/30)
	if err != nil {
		t.Fatal(err)
	}
	var replacements domain.TwoFactorRecoveryCodesResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/setup/confirmations", jsonInput(t, map[string]string{"flow": challenge.Flow, "code": code}), nil), 200, &replacements)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/two-factor/completions", jsonInput(t, map[string]string{"flow": challenge.Flow}), nil), 200, &tokens)
	// Assert
	if !status.IsEnabled || status.RemainingRecoveryCodes != 10 || len(codes.RecoveryCodes) != 10 || len(replacements.RecoveryCodes) != 10 || codes.RecoveryCodes[0] == replacements.RecoveryCodes[0] {
		t.Fatal("recovery lifecycle invariant violated")
	}
}
