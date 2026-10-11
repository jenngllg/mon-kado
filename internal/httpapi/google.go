package httpapi

import (
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/logging"
	"github.com/JennGllg/mon-kado/internal/providers"
	"github.com/JennGllg/mon-kado/internal/validation"
	"io"
	"mime"
	"net/http"
	"net/url"
	"time"
)

func (a *API) googleRoutes() {
	if a.Service.Config.GoogleEnabled {
		a.Google = providers.NewGoogle(a.Service.Config, a.Service.Now)
	}
	a.route("GET /api/v1/auth/google", options{Rate: 10}, a.startGoogle)
	a.route("POST /api/v1/auth/google/callback", options{Rate: 30}, a.googleCallback)
	a.route("POST /api/v1/auth/google/completions", options{CSRF: true, Rate: 10}, a.completeGoogle)
	a.route("POST /api/v1/auth/google/link", options{CSRF: true, Rate: 10}, a.completeGoogle)
}
func (a *API) googleAvailable() error {
	if !a.Service.Config.GoogleEnabled || a.Google == nil {
		return domain.Failure(503, domain.TechnicalDependencyUnavailable, "Google authentication is disabled.")
	}
	return nil
}
func (a *API) googleCookie(kind, value string, expires time.Time, callback bool) *http.Cookie {
	sameSite := http.SameSiteStrictMode
	if callback {
		sameSite = http.SameSiteNoneMode
	}
	cookie := &http.Cookie{Name: a.cookieName(kind), Value: value, Path: "/", HttpOnly: true, Secure: true, SameSite: sameSite, Expires: expires}
	if value == "" {
		cookie.MaxAge = -1
	}
	return cookie
}
func (a *API) startGoogle(r *request) (response, error) {
	if err := a.googleAvailable(); err != nil {
		return response{}, err
	}
	persistent, err := validation.GoogleQuery(r.URL.Query())
	if err != nil {
		return response{}, err
	}
	callback := a.Service.Config.PublicAPIOrigin + "/api/v1/auth/google/callback"
	challenge, err := a.Service.StartGoogle(r.Context(), callback, persistent, a.cookie(r.Request, "Refresh"))
	if err != nil {
		return response{}, err
	}
	query := url.Values{"client_id": {a.Service.Config.GoogleClientID}, "redirect_uri": {callback}, "response_type": {"code"}, "response_mode": {"form_post"}, "scope": {"openid email profile"}, "state": {challenge.State}, "nonce": {challenge.Nonce}, "code_challenge": {challenge.VerifierChallenge}, "code_challenge_method": {"S256"}}
	return response{Status: 302, Location: "https://accounts.google.com/o/oauth2/v2/auth?" + query.Encode(), Cookies: []*http.Cookie{a.googleCookie("GoogleCorrelation", challenge.Binding, challenge.Expires, true)}}, nil
}
func (a *API) googleCallback(r *request) (response, error) {
	if err := a.googleAvailable(); err != nil {
		return response{}, err
	}
	media, _, err := mime.ParseMediaType(r.Header.Get("Content-Type"))
	if err != nil || media != "application/x-www-form-urlencoded" {
		return response{}, domain.Failure(415, domain.RequestUnsupportedMediaType, "The callback requires a form body.")
	}
	content, err := io.ReadAll(io.LimitReader(r.Body, 4097))
	if err != nil {
		return response{}, domain.Failure(400, domain.RequestBadRequest, "The callback body is invalid.")
	}
	if len(content) > 4096 {
		return response{}, domain.Failure(413, domain.RequestPayloadTooLarge, "The callback body is too large.")
	}
	form, err := url.ParseQuery(string(content))
	expiry := time.Unix(1, 0)
	result := response{Status: 302, Location: a.Service.Config.FrontendOrigin + "/login/google-return#error=failed", Cookies: []*http.Cookie{a.googleCookie("GoogleCorrelation", "", expiry, true), a.googleCookie("GoogleExternal", "", expiry, false)}}
	if err != nil || len(form["state"]) != 1 || len(form["code"]) != 1 || form.Get("state") == "" || form.Get("code") == "" {
		if form.Get("error") == "access_denied" {
			result.Location = a.Service.Config.FrontendOrigin + "/login/google-return#error=cancelled"
		}
		return result, nil
	}
	binding := a.cookie(r.Request, "GoogleCorrelation")
	proof, err := a.Service.GoogleCallback(r.Context(), form.Get("state"), binding, form.Get("code"), a.Google)
	if err != nil {
		if r.Context().Err() != nil {
			return response{}, r.Context().Err()
		}
		var failure *domain.Error
		if errors.As(err, &failure) && failure.Status == 503 {
			result.Location = a.Service.Config.FrontendOrigin + "/login/google-return#error=unavailable"
		}
		a.Logger.ErrorContext(r.Context(), "Google callback rejected", "EventId", logging.GoogleCallbackRejected, "CorrelationId", logging.Correlation(r.Header.Get("X-Correlation-ID")), "TraceId", logging.Trace(r.Header.Get("X-Trace-ID")))
		return result, nil
	}
	result.Location = a.Service.Config.FrontendOrigin + "/login/google-return#flow=" + url.QueryEscape(proof)
	result.Cookies[1] = a.googleCookie("GoogleExternal", binding, a.Service.Now().UTC().Add(5*time.Minute), false)
	return result, nil
}
func (a *API) completeGoogle(r *request) (response, error) {
	if err := a.googleAvailable(); err != nil {
		return response{}, err
	}
	var flow string
	var password *string
	if r.URL.Path == "/api/v1/auth/google/link" {
		input, err := body[domain.LinkGoogleAccountRequest](r)
		if err != nil {
			return response{}, err
		}
		flow = *input.Flow
		password = input.CurrentPassword
	} else {
		input, err := body[domain.CompleteGoogleSessionRequest](r)
		if err != nil {
			return response{}, err
		}
		flow = *input.Flow
	}
	session, err := a.Service.CompleteGoogle(r.Context(), flow, a.cookie(r.Request, "GoogleExternal"), password)
	if err != nil {
		return response{}, err
	}
	result := response{Body: session.Access, Session: &session, Cookies: []*http.Cookie{a.googleCookie("GoogleExternal", "", time.Unix(1, 0), false)}}
	if session.Challenge != nil {
		result.Status = 202
		result.Body = session.Challenge
		result.Session = nil
	}
	return result, nil
}
