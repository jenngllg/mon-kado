package main

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"strings"
	"sync/atomic"
	"time"
)

// GmailAuthorizationBroker obtains a send-only refresh token using a loopback PKCE callback.
type GmailAuthorizationBroker interface {
	// Authorize binds offline consent to a native loopback callback without persisting secrets.
	Authorize(context.Context, string, string, io.Writer) (string, error)
}

// GmailOAuthBroker runs a native loopback listener and performs one bounded token exchange.
type GmailOAuthBroker struct {
	HTTP                                 *http.Client
	AuthorizationEndpoint, TokenEndpoint string
}

// NewGmailOAuthBroker configures official Google endpoints without storing tokens.
func NewGmailOAuthBroker() *GmailOAuthBroker {
	return &GmailOAuthBroker{HTTP: &http.Client{Timeout: 15 * time.Second, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}, AuthorizationEndpoint: "https://accounts.google.com/o/oauth2/v2/auth", TokenEndpoint: "https://oauth2.googleapis.com/token"}
}

// Authorize opens an IPv4 loopback callback and asks the user to open the consent URL.
func (b *GmailOAuthBroker) Authorize(ctx context.Context, clientID, clientSecret string, output io.Writer) (string, error) {
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		return "", err
	}
	defer listener.Close()
	redirect := "http://" + listener.Addr().String() + "/oauth2/callback"
	state, verifier := randomToken(), randomToken()
	query := url.Values{"client_id": {clientID}, "redirect_uri": {redirect}, "response_type": {"code"}, "scope": {"https://www.googleapis.com/auth/gmail.send"}, "access_type": {"offline"}, "prompt": {"consent"}, "state": {state}, "code_challenge": {base64.RawURLEncoding.EncodeToString(hashToken(verifier))}, "code_challenge_method": {"S256"}}
	callbacks := make(chan string, 1)
	var callbackReceived atomic.Bool
	mux := http.NewServeMux()
	mux.HandleFunc("GET /oauth2/callback", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/plain; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		w.Header().Set("Referrer-Policy", "no-referrer")
		if r.Host != listener.Addr().String() || len(r.URL.Query()["state"]) != 1 || len(r.URL.Query()["code"]) != 1 || r.URL.Query().Get("code") == "" || subtle.ConstantTimeCompare([]byte(r.URL.Query().Get("state")), []byte(state)) != 1 {
			http.Error(w, "The authorization callback is invalid.", 400)
			return
		}
		if !callbackReceived.CompareAndSwap(false, true) {
			http.Error(w, "This authorization has already been received.", 409)
			return
		}
		select {
		case callbacks <- r.URL.Query().Get("code"):
			io.WriteString(w, "Authorization received. Return to the MonKado terminal.")
		default:
			http.Error(w, "This authorization has already been received.", 409)
		}
	})
	server := &http.Server{Handler: mux, ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 10 * time.Second, WriteTimeout: 10 * time.Second, MaxHeaderBytes: 8192}
	done := make(chan error, 1)
	go func() { done <- server.Serve(listener) }()
	defer server.Close()
	if _, err = fmt.Fprintln(output, "Open this URL in your browser:\n"+b.AuthorizationEndpoint+"?"+query.Encode()); err != nil {
		return "", err
	}
	var code string
	select {
	case code = <-callbacks:
	case <-ctx.Done():
		return "", ctx.Err()
	case err = <-done:
		return "", err
	}
	form := url.Values{"grant_type": {"authorization_code"}, "code": {code}, "code_verifier": {verifier}, "redirect_uri": {redirect}, "client_id": {clientID}, "client_secret": {clientSecret}}
	request, err := http.NewRequestWithContext(ctx, "POST", b.TokenEndpoint, strings.NewReader(form.Encode()))
	if err != nil {
		return "", err
	}
	request.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	response, err := b.HTTP.Do(request)
	if err != nil {
		return "", errors.New("Google token exchange is unavailable")
	}
	defer response.Body.Close()
	data, err := io.ReadAll(io.LimitReader(response.Body, 1024*1024+1))
	if err != nil || len(data) > 1024*1024 || response.StatusCode != 200 {
		return "", errors.New("Google token exchange failed")
	}
	var tokens struct {
		Refresh string `json:"refresh_token"`
		Scope   string `json:"scope"`
	}
	if json.Unmarshal(data, &tokens) != nil || tokens.Refresh == "" {
		return "", errors.New("Google did not return a refresh token; check the OAuth desktop client and offline consent")
	}
	if tokens.Scope != "" {
		found := false
		for _, scope := range strings.Fields(tokens.Scope) {
			if scope == "https://www.googleapis.com/auth/gmail.send" {
				found = true
			}
		}
		if !found {
			return "", errors.New("the Gmail send scope was not granted")
		}
	}
	return tokens.Refresh, nil
}

func randomToken() string {
	b := make([]byte, 32)
	rand.Read(b)
	return base64.RawURLEncoding.EncodeToString(b)
}
func hashToken(value string) []byte { hash := sha256.Sum256([]byte(value)); return hash[:] }
