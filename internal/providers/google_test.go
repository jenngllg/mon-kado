package providers

import (
	"crypto"
	"crypto/rand"
	"crypto/rsa"
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/coreos/go-oidc/v3/oidc"
	"github.com/golang-jwt/jwt/v5"
	"net/http"
	"testing"
	"time"
)

func TestExchange_WhenSignedIdentityInvalid_RejectsUntrustedClaimsWithoutPOSTRetry(t *testing.T) {
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatal(err)
	}
	now := time.Date(2026, 10, 11, 12, 0, 0, 0, time.UTC)
	for _, test := range []struct {
		Name, Claim string
		Value       any
		Valid       bool
	}{
		{"valid", "name", "A member", true}, {"wrongIssuer", "iss", "https://attacker.example", false}, {"wrongAudience", "aud", "different-client", false}, {"expired", "exp", now.Add(-time.Hour).Unix(), false}, {"wrongNonce", "nonce", "other-nonce", false}, {"unverified", "email_verified", false, false}, {"invalidEmail", "email", "Member <member@gmail.com>", false}, {"wrongAuthorizedParty", "azp", "other-client", false}, {"missingSubject", "sub", "", false},
	} {
		t.Run(test.Name, func(t *testing.T) {
			// Arrange
			claims := jwt.MapClaims{"iss": "https://accounts.google.com", "aud": "client", "sub": "google-subject", "exp": now.Add(time.Hour).Unix(), "iat": now.Unix(), "nonce": "expected-nonce", "email": "member@gmail.com", "email_verified": true, "name": "A member", "azp": "client"}
			claims[test.Claim] = test.Value
			token, err := jwt.NewWithClaims(jwt.SigningMethodRS256, claims).SignedString(key)
			if err != nil {
				t.Fatal(err)
			}
			g := NewGoogle(config.Config{GoogleClientID: "client", GoogleClientSecret: "secret", HTTPTimeout: time.Second}, func() time.Time { return now })
			g.Verifier = oidc.NewVerifier("https://accounts.google.com", &oidc.StaticKeySet{PublicKeys: []crypto.PublicKey{&key.PublicKey}}, &oidc.Config{ClientID: "client", SupportedSigningAlgs: []string{"RS256"}, Now: func() time.Time { return now }})
			calls := 0
			g.HTTP.Transport = transportFunc(func(r *http.Request) (*http.Response, error) {
				calls++
				if r.Method != "POST" {
					t.Fatal("OAuth exchange method changed")
				}
				if err := r.ParseForm(); err != nil || r.Form.Get("code_verifier") != "pkce-verifier" || r.Form.Get("redirect_uri") != "https://api.example.com/callback" {
					t.Fatal("OAuth PKCE or callback missing")
				}
				data, _ := json.Marshal(map[string]string{"id_token": token})
				return providerResponse(200, string(data)), nil
			})
			// Act
			identity, err := g.Exchange(t.Context(), "code", "pkce-verifier", "expected-nonce", "https://api.example.com/callback")
			// Assert
			if (err == nil) != test.Valid || calls != 1 {
				t.Fatalf("identity validation differs: valid %t error %v calls %d", test.Valid, err, calls)
			}
			if test.Valid && (identity.Subject != "google-subject" || identity.Email != "member@gmail.com") {
				t.Fatal("verified identity mapping differs")
			}
		})
	}
}
