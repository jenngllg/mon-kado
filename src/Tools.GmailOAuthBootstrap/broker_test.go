package main

import (
	"context"
	"crypto/sha256"
	"encoding/base64"
	"errors"
	"io"
	"net/http"
	"net/url"
	"strings"
	"testing"
)

type consentWriter struct {
	T         *testing.T
	Challenge string
	Callback  string
}

func (w *consentWriter) Write(data []byte) (int, error) {
	w.T.Helper()
	text := string(data)
	_, link, found := strings.Cut(text, "\n")
	if !found {
		w.T.Fatal("consent URL missing")
	}
	u, err := url.Parse(strings.TrimSpace(link))
	if err != nil {
		w.T.Fatal(err)
	}
	query := u.Query()
	if query.Get("scope") != "https://www.googleapis.com/auth/gmail.send" || query.Get("code_challenge_method") != "S256" || query.Get("access_type") != "offline" {
		w.T.Fatal("send-only PKCE consent contract differs")
	}
	w.Challenge = query.Get("code_challenge")
	w.Callback = query.Get("redirect_uri")
	callback, err := url.Parse(w.Callback)
	if err != nil || callback.Hostname() != "127.0.0.1" {
		w.T.Fatal("callback is not IPv4 loopback")
	}
	values := url.Values{"state": {"wrong-state"}, "code": {"authorization-code"}}
	for _, expected := range []int{400, 200, 409} {
		req, err := http.NewRequestWithContext(w.T.Context(), "GET", w.Callback+"?"+values.Encode(), nil)
		if err != nil {
			w.T.Fatal(err)
		}
		response, err := http.DefaultClient.Do(req)
		if err != nil {
			w.T.Fatal(err)
		}
		io.Copy(io.Discard, response.Body)
		response.Body.Close()
		if response.StatusCode != expected {
			w.T.Fatalf("callback status %d, expected %d", response.StatusCode, expected)
		}
		values.Set("state", query.Get("state"))
	}
	return len(data), nil
}

type failedOutput struct{}

func (failedOutput) Write([]byte) (int, error) { return 0, errors.New("output is unavailable") }

func TestAuthorize_WhenCancelledOrOutputUnavailable_StopsBeforeTokenExchange(t *testing.T) {
	for _, tc := range []struct {
		name   string
		output io.Writer
		cancel bool
	}{
		{"cancelled", io.Discard, true}, {"outputUnavailable", failedOutput{}, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			ctx, cancel := context.WithCancel(t.Context())
			defer cancel()
			if tc.cancel {
				cancel()
			}
			broker := NewGmailOAuthBroker()
			calls := 0
			broker.HTTP.Transport = tokenTransport(func(*http.Request) (*http.Response, error) { calls++; return nil, errors.New("unexpected exchange") })
			// Act
			token, err := broker.Authorize(ctx, "desktop-client", "secret", tc.output)
			// Assert
			if token != "" || err == nil || calls != 0 {
				t.Fatal("failed consent reached the provider")
			}
			if tc.cancel && !errors.Is(err, context.Canceled) {
				t.Fatal("cancellation was substituted")
			}
		})
	}
}

func TestAuthorize_WhenTokenResponseInvalid_RejectsWithoutRetryOrProviderDetails(t *testing.T) {
	for _, tc := range []struct {
		name, body string
		status     int
		network    bool
	}{
		{"network", "", 0, true},
		{"unauthorized", `{"error":"private-provider-details"}`, 401, false},
		{"oversized", strings.Repeat("x", 1024*1024+1), 200, false},
		{"malformedJSON", "{private-provider-details", 200, false},
		{"missingRefresh", `{"access_token":"private-provider-details"}`, 200, false},
		{"wrongScope", `{"refresh_token":"private-provider-details","scope":"openid profile"}`, 200, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			writer := &consentWriter{T: t}
			broker := NewGmailOAuthBroker()
			calls := 0
			broker.HTTP.Transport = tokenTransport(func(*http.Request) (*http.Response, error) {
				calls++
				if tc.network {
					return nil, errors.New("private-provider-details")
				}
				return &http.Response{StatusCode: tc.status, Body: io.NopCloser(strings.NewReader(tc.body)), Header: http.Header{}}, nil
			})
			// Act
			token, err := broker.Authorize(t.Context(), "desktop-client", "secret", writer)
			// Assert
			if token != "" || err == nil || calls != 1 || strings.Contains(err.Error(), "private-provider-details") {
				t.Fatal("invalid token response was accepted, retried or exposed")
			}
		})
	}
}

type tokenTransport func(*http.Request) (*http.Response, error)

func (f tokenTransport) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }

func TestAuthorize_WhenLoopbackConsentCompletes_ExchangesPKCEOnceWithoutPersistingSecrets(t *testing.T) {
	// Arrange
	writer := &consentWriter{T: t}
	broker := NewGmailOAuthBroker()
	calls := 0
	broker.HTTP.Transport = tokenTransport(func(r *http.Request) (*http.Response, error) {
		calls++
		if err := r.ParseForm(); err != nil {
			t.Fatal(err)
		}
		proof := sha256.Sum256([]byte(r.Form.Get("code_verifier")))
		if r.Method != "POST" || base64.RawURLEncoding.EncodeToString(proof[:]) != writer.Challenge || r.Form.Get("redirect_uri") != writer.Callback || r.Form.Get("code") != "authorization-code" {
			t.Fatal("token exchange is not bound to loopback consent")
		}
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(`{"refresh_token":"test-refresh","scope":"https://www.googleapis.com/auth/gmail.send"}`)), Header: http.Header{}}, nil
	})
	// Act
	token, err := broker.Authorize(t.Context(), "desktop-client", "test-secret", writer)
	// Assert
	if err != nil || token != "test-refresh" || calls != 1 {
		t.Fatalf("bootstrap failed: calls %d error %v", calls, err)
	}
}
