package httpapi

import (
	"crypto/subtle"
	"net/http"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func (a *API) cookieName(kind string) string {
	if a.Service.Config.Production {
		return "__Host-MonKado." + kind
	}
	return "MonKado." + kind
}
func (a *API) cookie(r *http.Request, kind string) string {
	c, err := r.Cookie(a.cookieName(kind))
	if err != nil {
		return ""
	}
	return c.Value
}
func (a *API) setCookie(w http.ResponseWriter, r *http.Request, kind, value string, expires time.Time, maxAge int) {
	http.SetCookie(w, &http.Cookie{Name: a.cookieName(kind), Value: value, Path: "/", HttpOnly: true, Secure: a.Service.Config.Production || r.TLS != nil, SameSite: http.SameSiteStrictMode, Expires: expires, MaxAge: maxAge})
}
func (a *API) sessionCookie(w http.ResponseWriter, r *http.Request, session *application.SessionResult) {
	expires := time.Time{}
	if session.Persistent {
		expires = session.ExpiresAt
	}
	maxAge := 0
	if session.RefreshToken == "" {
		expires = time.Unix(1, 0)
		maxAge = -1
	}
	a.setCookie(w, r, "Refresh", session.RefreshToken, expires, maxAge)
}
func (a *API) validCSRF(r *http.Request, member string) bool {
	cookie := a.cookie(r, "Csrf")
	parts := strings.Split(r.Header.Get("X-CSRF-TOKEN"), ".")
	if cookie == "" || len(parts) != 2 || parts[0] != cookie {
		return false
	}
	expected := a.Service.Crypto.Sign("csrf|" + cookie + "|" + member)
	return subtle.ConstantTimeCompare([]byte(parts[1]), []byte(expected)) == 1
}
func (a *API) registerCSRF() {
	a.mux.HandleFunc("GET /security/csrf-token", func(w http.ResponseWriter, r *http.Request) {
		member := ""
		if auth := r.Header.Get("Authorization"); strings.HasPrefix(auth, "Bearer ") {
			principal, err := a.Service.Authenticate(r.Context(), strings.TrimPrefix(auth, "Bearer "))
			if err != nil {
				a.writeError(w, r, err)
				return
			}
			member = principal.MemberID
		}
		token := security.RandomToken()
		a.setCookie(w, r, "Csrf", token, time.Time{}, 0)
		writeJSON(w, 200, domain.CsrfTokenResponse{Token: token + "." + a.Service.Crypto.Sign("csrf|"+token+"|"+member)})
	})
}
