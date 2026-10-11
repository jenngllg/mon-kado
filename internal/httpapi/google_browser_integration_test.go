//go:build integration

package httpapi_test

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"io"
	"log/slog"
	"net/http"
	"net/http/cookiejar"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
)

func TestGoogleBrowser_WhenReturningFormPOST_AcceptsGoogleOriginAndConsumesSecureBrowserProof(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.server.Close()
	f.service.Config.Production = true
	f.service.Config.GoogleEnabled = true
	f.service.Config.GoogleClientID = "browser-client"
	f.service.Config.GoogleClientSecret = "browser-secret"
	f.service.Config.FrontendOrigin = "https://frontend.example.test"
	f.service.Config.AllowedOrigins = []string{"https://frontend.example.test"}
	api := httpapi.New(f.service, slog.New(slog.NewJSONHandler(io.Discard, nil)))
	provider := &googleFake{Identity: application.GoogleIdentity{Subject: "browser-google-subject", Email: "browser@gmail.com", DisplayName: "Browser member"}}
	api.Google = provider
	f.server = httptest.NewTLSServer(api)
	t.Cleanup(f.server.Close)
	f.service.Config.PublicAPIOrigin = f.server.URL
	f.client = f.server.Client()
	f.client.Jar, _ = cookiejar.New(nil)
	f.client.CheckRedirect = func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }
	f.csrfToken(t)
	started := f.send(t, "GET", "/api/v1/auth/google?returnPath=%2Flogin%2Fgoogle-return&rememberMe=true", "", nil)
	location, err := url.Parse(started.Header.Get("Location"))
	if err != nil {
		t.Fatal(err)
	}
	var correlation *http.Cookie
	for _, cookie := range started.Cookies() {
		if strings.HasSuffix(cookie.Name, ".GoogleCorrelation") {
			correlation = cookie
		}
	}
	requireStatus(t, started, 302, nil)
	if correlation == nil || !correlation.Secure || !correlation.HttpOnly || correlation.SameSite != http.SameSiteNoneMode || !strings.HasPrefix(correlation.Name, "__Host-") || location.Host != "accounts.google.com" || location.Query().Get("code_challenge_method") != "S256" {
		t.Fatal("Google initiation or correlation cookie differs")
	}
	callback := func(origin string) *http.Response {
		request, err := http.NewRequestWithContext(t.Context(), "POST", f.server.URL+"/api/v1/auth/google/callback", strings.NewReader(url.Values{"state": {location.Query().Get("state")}, "code": {"browser-code"}}.Encode()))
		if err != nil {
			t.Fatal(err)
		}
		request.Header.Set("Content-Type", "application/x-www-form-urlencoded")
		request.Header.Set("Origin", origin)
		r, err := f.client.Do(request)
		if err != nil {
			t.Fatal(err)
		}
		return r
	}
	// Act
	requireStatus(t, callback("https://attacker.test"), 403, nil)
	returned := callback("https://accounts.google.com")
	returnedLocation, err := url.Parse(returned.Header.Get("Location"))
	if err != nil {
		t.Fatal(err)
	}
	proof, err := url.ParseQuery(returnedLocation.Fragment)
	if err != nil {
		t.Fatal(err)
	}
	var external *http.Cookie
	for _, cookie := range returned.Cookies() {
		if strings.HasSuffix(cookie.Name, ".GoogleExternal") {
			external = cookie
		}
	}
	requireStatus(t, returned, 302, nil)
	var session domain.AccessTokenResponse
	completion := jsonInput(t, map[string]string{"flow": proof.Get("flow")})
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/google/completions", completion, nil), 200, &session)
	f.access = session.AccessToken
	var current domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &current)
	// Assert
	if provider.Calls != 1 || current.Email != "browser@gmail.com" || external == nil || external.SameSite != http.SameSiteStrictMode || !external.Secure || proof.Get("flow") == "" {
		t.Fatal("Google callback browser binding or new account differs")
	}
	f.access = ""
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/google/completions", completion, nil), 401, nil)
	replayed := callback("https://accounts.google.com")
	if !strings.HasSuffix(replayed.Header.Get("Location"), "#error=failed") {
		t.Fatal("callback replay was accepted")
	}
	requireStatus(t, replayed, 302, nil)
	if provider.Calls != 1 {
		t.Fatal("callback replay repeated the token exchange")
	}
}
