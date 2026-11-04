package providers

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"encoding/base64"
	"errors"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/golang-jwt/jwt/v5"
	"io"
	"net"
	"net/http"
	"strings"
	"testing"
	"time"
)

type pushTransport func(*http.Request) (*http.Response, error)

func (f pushTransport) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }
func TestPush_WhenSending_UsesEmptyBoundedVAPIDRequestAndPreservesContext(t *testing.T) {
	// Arrange
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	now := time.Now().UTC()
	cfg := config.Config{HTTPTimeout: time.Second, PushOptions: config.PushOptions{PrivateKey: base64.RawURLEncoding.EncodeToString(key.D.FillBytes(make([]byte, 32))), PublicKey: base64.RawURLEncoding.EncodeToString(elliptic.Marshal(elliptic.P256(), key.X, key.Y)), Subject: "https://monkado.fr"}}
	provider := NewPush(cfg, func() time.Time { return now })
	calls := 0
	type contextKey struct{}
	ctx := context.WithValue(t.Context(), contextKey{}, "push-context")
	provider.Client.Transport = pushTransport(func(r *http.Request) (*http.Response, error) {
		calls++
		if r.Context().Value(contextKey{}) != "push-context" || r.Method != "POST" || r.Body != nil || r.Header.Get("TTL") != "300" || r.Header.Get("Topic") != "monkado-notifications" {
			t.Fatal("incorrect visible empty push")
		}
		auth := strings.TrimPrefix(r.Header.Get("Authorization"), "vapid t=")
		parts := strings.Split(auth, ", k=")
		if len(parts) != 2 || parts[1] != cfg.PushOptions.PublicKey {
			t.Fatal("invalid VAPID header")
		}
		token, err := jwt.Parse(parts[0], func(*jwt.Token) (any, error) { return &key.PublicKey, nil }, jwt.WithAudience("https://fcm.googleapis.com"), jwt.WithTimeFunc(func() time.Time { return now }), jwt.WithValidMethods([]string{"ES256"}))
		if err != nil || !token.Valid {
			t.Fatal("invalid signed VAPID proof", err)
		}
		return &http.Response{StatusCode: 201, Body: io.NopCloser(strings.NewReader(""))}, nil
	})
	// Act / Assert
	if status, err := provider.Send(ctx, "https://fcm.googleapis.com/device"); err != nil || status != 201 || calls != 1 {
		t.Fatal("send failed", err)
	}
	if _, err := provider.Send(ctx, "https://127.0.0.1/device"); err == nil || calls != 1 {
		t.Fatal("SSRF endpoint reached transport")
	}
	provider.Client.Transport = pushTransport(func(*http.Request) (*http.Response, error) { return nil, errors.New("secret capability URL") })
	if _, err := provider.Send(ctx, "https://fcm.googleapis.com/device"); err == nil || strings.Contains(err.Error(), "secret") {
		t.Fatal("provider error leaked")
	}
	provider.Options.PrivateKey = "!"
	if _, err := provider.Send(ctx, "https://fcm.googleapis.com/device"); err == nil {
		t.Fatal("bad key accepted")
	}
}
func TestPush_WhenConstructed_DisablesProxiesRedirectsAndTransparentRetries(t *testing.T) {
	p := NewPush(config.Config{HTTPTimeout: 3 * time.Second}, time.Now)
	if p.Client.Timeout != 3*time.Second || p.Client.Transport.(*http.Transport).Proxy != nil || p.Client.CheckRedirect(nil, nil) != http.ErrUseLastResponse {
		t.Fatal("unsafe push transport")
	}
}

func TestPushTransport_WhenAddressOrDNSIsUnsafe_RejectsBeforeAnyNetworkConnection(t *testing.T) {
	// Arrange: replace DNS only; no request reaches a real provider in this test.
	original := net.DefaultResolver
	net.DefaultResolver = &net.Resolver{PreferGo: true, Dial: func(context.Context, string, string) (net.Conn, error) {
		return nil, errors.New("simulated DNS outage")
	}}
	t.Cleanup(func() { net.DefaultResolver = original })
	transport := NewPush(config.Config{HTTPTimeout: time.Second}, time.Now).Client.Transport.(*http.Transport)
	for _, scenario := range []struct{ address, message string }{
		{"bad-address", "invalid push address"},
		{"fcm.googleapis.com:443", "push DNS failed"},
		{"127.0.0.1:443", "push DNS is not public"},
		{"[::1]:443", "push DNS is not public"},
		{"169.254.169.254:443", "push DNS is not public"},
	} {
		t.Run(scenario.address, func(t *testing.T) {
			// Act
			connection, err := transport.DialContext(t.Context(), "tcp", scenario.address)
			// Assert
			if connection != nil || err == nil || err.Error() != scenario.message {
				t.Fatal("unsafe dial was not rejected with a fixed diagnostic")
			}
		})
	}
	// A cancelled caller cannot establish a connection even to a checked public IP.
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	connection, err := transport.DialContext(ctx, "tcp", "1.1.1.1:443")
	if connection != nil || err == nil {
		t.Fatal("cancelled public dial established a connection")
	}
}
