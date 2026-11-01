// Package providers confines external HTTP protocols to typed clients.
package providers

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"html"
	"io"
	"mime"
	"net/http"
	"net/mail"
	"net/url"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/google/uuid"
)

// GmailClient is the typed Gmail transport with a cached OAuth access token.
type GmailClient struct {
	HTTP     *http.Client
	Config   config.Config
	Now      func() time.Time
	TokenURL string
	SendURL  string
	mu       sync.Mutex
	token    string
	expires  time.Time
	circuit  circuit
}

// NewGmail validates provider credentials and builds a bounded client with no redirect replay.
func NewGmail(cfg config.Config, now func() time.Time) (*GmailClient, error) {
	address, err := mail.ParseAddress(cfg.GmailSender)
	if err != nil || address.Address != cfg.GmailSender || cfg.GmailClientID == "" || cfg.GmailClientSecret == "" || cfg.GmailRefreshToken == "" {
		return nil, errors.New("Gmail sender and OAuth credentials are required")
	}
	return &GmailClient{Config: cfg, Now: now, TokenURL: "https://oauth2.googleapis.com/token", SendURL: "https://gmail.googleapis.com/gmail/v1/users/me/messages/send", HTTP: &http.Client{Timeout: cfg.HTTPTimeout, CheckRedirect: func(req *http.Request, via []*http.Request) error { return http.ErrUseLastResponse }}}, nil
}

func (g *GmailClient) accessToken(ctx context.Context) (string, error) {
	g.mu.Lock()
	defer g.mu.Unlock()
	if g.token != "" && g.expires.After(g.Now().Add(time.Minute)) {
		return g.token, nil
	}
	form := url.Values{"client_id": {g.Config.GmailClientID}, "client_secret": {g.Config.GmailClientSecret}, "refresh_token": {g.Config.GmailRefreshToken}, "grant_type": {"refresh_token"}}
	req, err := http.NewRequestWithContext(ctx, "POST", g.TokenURL, strings.NewReader(form.Encode()))
	if err != nil {
		return "", err
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	r, err := g.HTTP.Do(req)
	if err != nil {
		if ctx.Err() != nil {
			return "", ctx.Err()
		}
		return "", &application.DeliveryError{Category: "network", Retryable: true}
	}
	defer r.Body.Close()
	if r.StatusCode != 200 {
		return "", classifyFailure(r, g.Now())
	}
	var token struct {
		AccessToken string `json:"access_token"`
		ExpiresIn   int    `json:"expires_in"`
	}
	if err = json.NewDecoder(io.LimitReader(r.Body, 65536)).Decode(&token); err != nil || token.AccessToken == "" || token.ExpiresIn <= 0 {
		return "", &application.DeliveryError{Category: "invalidResponse", Retryable: true}
	}
	g.token, g.expires = token.AccessToken, g.Now().Add(time.Duration(token.ExpiresIn)*time.Second)
	return g.token, nil
}

// Send submits a single RFC 5322 message; the outbox owns every subsequent attempt.
func (g *GmailClient) Send(ctx context.Context, id string, message application.EmailMessage) (providerID string, sendErr error) {
	if err := g.circuit.allow(g.Now()); err != nil {
		return "", err
	}
	threshold, duration := g.Config.ProviderFailureThreshold, g.Config.ProviderBreakDuration
	if threshold == 0 {
		threshold = 5
	}
	if duration == 0 {
		duration = 30 * time.Second
	}
	defer func() { g.circuit.record(g.Now(), sendErr, threshold, duration) }()
	address, err := mail.ParseAddress(message.To)
	_, idErr := uuid.Parse(id)
	if err != nil || address.Address != message.To || strings.ContainsAny(message.Subject, "\r\n") || idErr != nil {
		return "", &application.DeliveryError{Category: "invalidMessage", Retryable: false}
	}
	token, err := g.accessToken(ctx)
	if err != nil {
		return "", err
	}
	text := strings.ReplaceAll(strings.ReplaceAll(message.Text, "\r\n", "\n"), "\n", "\r\n")
	boundary := "monkado-" + strings.ReplaceAll(id, "-", "")
	htmlBody := "<!doctype html><html lang=\"fr\"><body><p>" + strings.ReplaceAll(html.EscapeString(message.Text), "\n", "<br>") + "</p>"
	if message.ActionURL != "" {
		htmlBody += "<p><a href=\"" + html.EscapeString(message.ActionURL) + "\">Ouvrir MonKado</a></p>"
	}
	htmlBody += "</body></html>"
	raw := fmt.Sprintf("From: %s\r\nTo: %s\r\nSubject: %s\r\nMessage-ID: <%s@mon-kado.fr>\r\nMIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=\"%s\"\r\n\r\n--%s\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n%s\r\n--%s\r\nContent-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n%s\r\n--%s--\r\n", g.Config.GmailSender, message.To, mime.QEncoding.Encode("utf-8", message.Subject), strings.ReplaceAll(id, "-", ""), boundary, boundary, mimeBase64(text), boundary, mimeBase64(htmlBody), boundary)
	data, err := json.Marshal(map[string]string{"raw": base64.RawURLEncoding.EncodeToString([]byte(raw))})
	if err != nil {
		return "", err
	}
	req, err := http.NewRequestWithContext(ctx, "POST", g.SendURL, strings.NewReader(string(data)))
	if err != nil {
		return "", err
	}
	req.Header.Set("Authorization", "Bearer "+token)
	req.Header.Set("Content-Type", "application/json")
	r, err := g.HTTP.Do(req)
	if err != nil {
		if ctx.Err() != nil {
			return "", ctx.Err()
		}
		return "", &application.DeliveryError{Category: "network", Retryable: true}
	}
	defer r.Body.Close()
	if r.StatusCode != 200 {
		if r.StatusCode == 401 {
			g.mu.Lock()
			g.token = ""
			g.mu.Unlock()
		}
		return "", classifyFailure(r, g.Now())
	}
	var result struct {
		ID string `json:"id"`
	}
	if err = json.NewDecoder(io.LimitReader(r.Body, 65536)).Decode(&result); err != nil || result.ID == "" {
		return "", &application.DeliveryError{Category: "invalidResponse", Retryable: true}
	}
	return result.ID, nil
}

func mimeBase64(value string) string {
	encoded := base64.StdEncoding.EncodeToString([]byte(value))
	var folded strings.Builder
	for len(encoded) > 76 {
		folded.WriteString(encoded[:76])
		folded.WriteString("\r\n")
		encoded = encoded[76:]
	}
	folded.WriteString(encoded)
	return folded.String()
}

func classifyFailure(r *http.Response, now time.Time) error {
	failure := &application.DeliveryError{Category: "providerRejected", Retryable: r.StatusCode == 408 || r.StatusCode == 429 || r.StatusCode >= 500}
	if r.StatusCode == 401 || r.StatusCode == 403 {
		failure.Category = "authorization"
	}
	if r.StatusCode == 429 {
		failure.Category = "rateLimit"
	}
	if seconds, err := strconv.Atoi(r.Header.Get("Retry-After")); err == nil && seconds > 0 {
		failure.RetryAfter = time.Duration(min(seconds, 86400)) * time.Second
	} else if date, err := http.ParseTime(r.Header.Get("Retry-After")); err == nil && date.After(now) {
		failure.RetryAfter = date.Sub(now)
	}
	return failure
}
