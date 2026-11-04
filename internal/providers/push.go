package providers

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"encoding/base64"
	"errors"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/golang-jwt/jwt/v5"
	"io"
	"math/big"
	"net"
	"net/http"
	"net/netip"
	"net/url"
	"time"
)

// Push implements RFC 8030 empty-body signals authenticated with RFC 8292 VAPID.
// Empty messages carry no business data and require no content-encryption keys.
type Push struct {
	Client  *http.Client
	Options config.PushOptions
	Now     func() time.Time
}

// NewPush builds a bounded transport with no redirects, retries or process proxies.
func NewPush(cfg config.Config, now func() time.Time) *Push {
	transport := &http.Transport{MaxIdleConns: 10, MaxIdleConnsPerHost: 2, IdleConnTimeout: time.Minute, TLSHandshakeTimeout: cfg.HTTPTimeout, ResponseHeaderTimeout: cfg.HTTPTimeout, DialContext: pushDial}
	return &Push{Client: &http.Client{Transport: transport, Timeout: cfg.HTTPTimeout, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}, Options: cfg.PushOptions, Now: now}
}
func pushDial(ctx context.Context, network, address string) (net.Conn, error) {
	host, port, err := net.SplitHostPort(address)
	if err != nil {
		return nil, errors.New("invalid push address")
	}
	ips, err := net.DefaultResolver.LookupNetIP(ctx, "ip", host)
	if err != nil || len(ips) == 0 {
		return nil, errors.New("push DNS failed")
	}
	for _, ip := range ips {
		ip = ip.Unmap()
		if !ip.IsGlobalUnicast() || ip.IsPrivate() || ip.IsLoopback() || ip.IsLinkLocalUnicast() || ip == netip.MustParseAddr("169.254.169.254") {
			return nil, errors.New("push DNS is not public")
		}
	}
	// Pin an address from the checked set; TLS still authenticates the original service hostname.
	return (&net.Dialer{}).DialContext(ctx, network, net.JoinHostPort(ips[0].String(), port))
}

// Send attempts a single request; neither endpoint nor raw provider errors escape this boundary.
func (p *Push) Send(ctx context.Context, endpoint string) (int, error) {
	if !domain.ValidPushEndpoint(endpoint) {
		return 0, errors.New("invalid push endpoint")
	}
	raw, err := base64.RawURLEncoding.DecodeString(p.Options.PrivateKey)
	if err != nil {
		return 0, errors.New("invalid VAPID key")
	}
	x, y := elliptic.P256().ScalarBaseMult(raw)
	key := &ecdsa.PrivateKey{PublicKey: ecdsa.PublicKey{Curve: elliptic.P256(), X: x, Y: y}, D: new(big.Int).SetBytes(raw)}
	u, _ := url.Parse(endpoint)
	token := jwt.NewWithClaims(jwt.SigningMethodES256, jwt.MapClaims{"aud": u.Scheme + "://" + u.Host, "exp": p.Now().UTC().Add(time.Hour).Unix(), "sub": p.Options.Subject})
	signed, err := token.SignedString(key)
	if err != nil {
		return 0, errors.New("push authorization failed")
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodPost, endpoint, nil)
	if err != nil {
		return 0, errors.New("push request failed")
	}
	request.Header.Set("Authorization", "vapid t="+signed+", k="+p.Options.PublicKey)
	// A short TTL limits stale lock-screen signals. A stable topic coalesces outstanding signals.
	request.Header.Set("TTL", "300")
	request.Header.Set("Urgency", "normal")
	request.Header.Set("Topic", "monkado-notifications")
	response, err := p.Client.Do(request)
	if err != nil {
		return 0, errors.New("push transport failed")
	}
	defer response.Body.Close()
	_, _ = io.Copy(io.Discard, io.LimitReader(response.Body, 4096))
	return response.StatusCode, nil
}
