package providers

import (
	"context"
	"crypto/subtle"
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/coreos/go-oidc/v3/oidc"
	"io"
	"net/http"
	"net/mail"
	"net/url"
	"strings"
	"time"
	"unicode/utf8"
)

// Google verifies official signed OIDC identities through a bounded typed client.
type Google struct {
	HTTP          *http.Client
	Config        config.Config
	Verifier      *oidc.IDTokenVerifier
	TokenEndpoint string
}

// NewGoogle creates a lazy JWK verifier without a startup network request.
func NewGoogle(cfg config.Config, now func() time.Time) *Google {
	client := &http.Client{Timeout: cfg.HTTPTimeout, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}
	keys := oidc.NewRemoteKeySet(oidc.ClientContext(context.Background(), client), "https://www.googleapis.com/oauth2/v3/certs")
	return &Google{HTTP: client, Config: cfg, Verifier: oidc.NewVerifier("https://accounts.google.com", keys, &oidc.Config{ClientID: cfg.GoogleClientID, SupportedSigningAlgs: []string{"RS256"}, Now: now}), TokenEndpoint: "https://oauth2.googleapis.com/token"}
}

// Exchange performs one POST and verifies the resulting nonce-bound ID token.
func (g *Google) Exchange(ctx context.Context, code, verifier, nonce, redirect string) (application.GoogleIdentity, error) {
	failure := func() (application.GoogleIdentity, error) {
		return application.GoogleIdentity{}, domain.Failure(401, domain.GoogleAuthenticationFailed, "The Google identity proof is invalid.")
	}
	form := url.Values{"grant_type": {"authorization_code"}, "code": {code}, "code_verifier": {verifier}, "redirect_uri": {redirect}, "client_id": {g.Config.GoogleClientID}, "client_secret": {g.Config.GoogleClientSecret}}
	request, err := http.NewRequestWithContext(ctx, "POST", g.TokenEndpoint, strings.NewReader(form.Encode()))
	if err != nil {
		return failure()
	}
	request.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	response, err := g.HTTP.Do(request)
	if err != nil {
		return application.GoogleIdentity{}, domain.Failure(503, domain.TechnicalDependencyUnavailable, "The Google provider is unavailable.")
	}
	defer response.Body.Close()
	data, err := io.ReadAll(io.LimitReader(response.Body, 1024*1024+1))
	if err != nil || len(data) > 1024*1024 {
		return failure()
	}
	if response.StatusCode != 200 {
		if response.StatusCode == 429 || response.StatusCode >= 500 {
			return application.GoogleIdentity{}, domain.Failure(503, domain.TechnicalDependencyUnavailable, "The Google provider is unavailable.")
		}
		return failure()
	}
	var token struct {
		ID string `json:"id_token"`
	}
	if json.Unmarshal(data, &token) != nil || token.ID == "" {
		return failure()
	}
	verified, err := g.Verifier.Verify(oidc.ClientContext(ctx, g.HTTP), token.ID)
	if err != nil {
		return failure()
	}
	if subtle.ConstantTimeCompare([]byte(verified.Nonce), []byte(nonce)) != 1 {
		return failure()
	}
	var claims struct {
		Email           string `json:"email"`
		Verified        bool   `json:"email_verified"`
		Name            string `json:"name"`
		Domain          string `json:"hd"`
		AuthorizedParty string `json:"azp"`
	}
	if verified.Claims(&claims) != nil || !claims.Verified || verified.Subject == "" || claims.AuthorizedParty != "" && claims.AuthorizedParty != g.Config.GoogleClientID {
		return failure()
	}
	address, err := mail.ParseAddress(claims.Email)
	if err != nil || address.Address != claims.Email || len(claims.Email) > 254 {
		return failure()
	}
	name := strings.Join(strings.Fields(claims.Name), " ")
	if utf8.RuneCountInString(name) > 80 {
		name = "MonKado member"
	}
	return application.GoogleIdentity{Subject: verified.Subject, Email: claims.Email, DisplayName: name, HostedDomain: claims.Domain}, nil
}
