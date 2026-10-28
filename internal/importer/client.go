// Package importer extracts best-effort merchant previews through a public-only HTTP connector.
package importer

import (
	"bytes"
	"context"
	"crypto/tls"
	"errors"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"golang.org/x/net/html/charset"
	"golang.org/x/net/idna"
	"io"
	"mime"
	"net"
	"net/http"
	"net/netip"
	"net/url"
	"strings"
	"time"
)

// Resolver resolves the complete address set before any merchant connection.
type Resolver interface {
	// LookupNetIP returns all candidate addresses using the caller's DNS context.
	LookupNetIP(context.Context, string, string) ([]netip.Addr, error)
}

// Dialer establishes a connection only to a validated numeric destination.
type Dialer interface {
	// DialContext opens the already validated destination using the caller's context.
	DialContext(context.Context, string, string) (net.Conn, error)
}

// Keep the standard transport token and identify the application without impersonating a browser.
const merchantUserAgent = "Go-http-client/1.1 MonKado/1.0 (+https://www.monkado.fr)"

// Client owns the resolver and dialer used for every redirect and image request.
type Client struct {
	cache            *previewCache
	PageSource       PageSource
	FallbackSource   PageSource
	ImageSource      ImageSource
	FinalImageSource ImageSource
	Resolver         Resolver
	Dialer           Dialer
	Timeout          time.Duration
	MaximumRedirects int
	MaximumHTMLBytes int
	Wait             RetryWait
}

// New creates an isolated client that ignores process proxy settings.
func New() *Client {
	return &Client{Resolver: net.DefaultResolver, Dialer: &net.Dialer{Timeout: 10 * time.Second}, Timeout: config.MaximumImportDuration, MaximumRedirects: 3, MaximumHTMLBytes: config.MaximumImportHTMLBytes}
}

var excluded = func() []netip.Prefix {
	var result []netip.Prefix
	for _, cidr := range []string{"0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/3", "2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20"} {
		result = append(result, netip.MustParsePrefix(cidr))
	}
	return result
}()

// PublicAddress rejects private, mapped private and special-purpose addresses.
func PublicAddress(address netip.Addr) bool {
	address = address.Unmap()
	if !address.IsValid() || address.Zone() != "" || !address.IsGlobalUnicast() {
		return false
	}
	if address.Is6() && !netip.MustParsePrefix("2000::/3").Contains(address) {
		return false
	}
	for _, prefix := range excluded {
		if prefix.Contains(address) {
			return false
		}
	}
	return true
}

func rejected() error {
	return domain.Failure(400, domain.WishImportUrlRejected, "The URL must resolve exclusively to public web addresses.")
}
func webURL(value string) (*url.URL, error) {
	u, err := url.Parse(value)
	if err != nil || u.Hostname() == "" || u.User != nil || u.Scheme != "http" && u.Scheme != "https" || strings.ContainsAny(value, "\r\n\t ") || strings.Contains(u.Hostname(), "%") {
		return nil, rejected()
	}
	if port := u.Port(); port != "" && !(u.Scheme == "http" && port == "80") && !(u.Scheme == "https" && port == "443") {
		return nil, rejected()
	}
	return u, nil
}
func (c *Client) addresses(ctx context.Context, host string) ([]netip.Addr, error) {
	addresses, err := c.Resolver.LookupNetIP(ctx, "ip", host)
	if err != nil {
		return nil, err
	}
	if len(addresses) == 0 {
		return nil, errors.New("merchant host has no addresses")
	}
	for _, address := range addresses {
		if !PublicAddress(address) {
			return nil, rejected()
		}
	}
	return addresses, nil
}

// Validate performs public-only validation before a preview can start.
func (c *Client) Validate(ctx context.Context, value string) error {
	u, err := webURL(value)
	if err != nil {
		return err
	}
	_, err = c.addresses(ctx, u.Hostname())
	return err
}

func (c *Client) downloadAttempt(ctx context.Context, value string, limit int64, accept string) ([]byte, *url.URL, string, error) {
	for redirects := 0; ; redirects++ {
		u, err := webURL(value)
		if err != nil {
			return nil, nil, "", err
		}
		response, err := c.request(ctx, u, accept)
		if err != nil {
			return nil, nil, "", err
		}
		if response.StatusCode >= 300 && response.StatusCode <= 399 {
			location, err := url.Parse(response.Header.Get("Location"))
			response.Body.Close()
			if err != nil || response.Header.Get("Location") == "" || redirects >= c.MaximumRedirects {
				return nil, nil, "", errors.New("merchant redirect limit exceeded")
			}
			value = u.ResolveReference(location).String()
			continue
		}
		if response.StatusCode < 200 || response.StatusCode >= 300 {
			response.Body.Close()
			return nil, nil, "", merchantStatusError(response.StatusCode, response.Header.Get("Retry-After"))
		}
		content, err := io.ReadAll(io.LimitReader(response.Body, limit+1))
		response.Body.Close()
		if err != nil {
			return nil, nil, "", err
		}
		if int64(len(content)) > limit {
			return nil, nil, "", errors.New("merchant response is unavailable or too large")
		}
		media, parameters, _ := mime.ParseMediaType(response.Header.Get("Content-Type"))
		if (media == "text/html" || media == "application/xhtml+xml") && parameters["charset"] != "" {
			if reader, err := charset.NewReaderLabel(strings.Trim(parameters["charset"], `"`), bytes.NewReader(content)); err == nil {
				content, err = io.ReadAll(io.LimitReader(reader, 3*limit+1))
				if err != nil || int64(len(content)) > 3*limit {
					return nil, nil, "", errors.New("merchant text is unavailable or too large")
				}
			}
		}
		return content, u, media, nil
	}
}

func (c *Client) request(ctx context.Context, target *url.URL, accept string) (*http.Response, error) {
	headers := make(http.Header)
	if accept != "" {
		headers.Set("Accept", accept)
	}
	return c.requestWithHeaders(ctx, target, http.MethodGet, nil, headers)
}

func (c *Client) requestWithHeaders(ctx context.Context, target *url.URL, method string, body io.Reader, headers http.Header) (*http.Response, error) {
	return c.requestWithProtocol(ctx, target, method, body, headers, true)
}

func (c *Client) requestWithProtocol(ctx context.Context, target *url.URL, method string, body io.Reader, headers http.Header, http2 bool) (*http.Response, error) {
	host, err := idna.Lookup.ToASCII(target.Hostname())
	if err != nil {
		return nil, rejected()
	}
	addresses, err := c.addresses(ctx, host)
	if err != nil {
		return nil, err
	}
	scheme, port := "http", "80"
	if target.Scheme == "https" {
		scheme, port = "https", "443"
	}
	destination := net.JoinHostPort(addresses[0].String(), port)
	// Custom TLS settings and DNS pinning otherwise disable HTTP/2 negotiation.
	transport := &http.Transport{Proxy: nil, DisableKeepAlives: true, ForceAttemptHTTP2: http2, ResponseHeaderTimeout: c.Timeout,
		TLSClientConfig: &tls.Config{MinVersion: tls.VersionTLS12, ServerName: host},
		DialContext: func(ctx context.Context, network, address string) (net.Conn, error) {
			if address != destination {
				return nil, rejected()
			}
			var last error
			for _, ip := range addresses {
				connection, err := c.Dialer.DialContext(ctx, network, net.JoinHostPort(ip.String(), port))
				if err == nil {
					return connection, nil
				}
				last = err
				if ctx.Err() != nil {
					return nil, ctx.Err()
				}
			}
			return nil, last
		}}
	defer transport.CloseIdleConnections()
	client := &http.Client{Transport: transport, Timeout: c.Timeout, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}
	// Only the validated numeric destination reaches the transport URL; preserve merchant authority for Host and TLS.
	pinned := &url.URL{Scheme: scheme, Host: destination, Path: target.Path, RawPath: target.RawPath, RawQuery: target.RawQuery, ForceQuery: target.ForceQuery}
	req, err := http.NewRequestWithContext(ctx, method, pinned.String(), body)
	if err != nil {
		return nil, err
	}
	// Provider POSTs are admitted once; the transport must not replay their bodies.
	req.GetBody = nil
	req.Host = target.Host
	req.Header.Set("User-Agent", merchantUserAgent)
	for name, values := range headers {
		req.Header[name] = append([]string(nil), values...)
	}
	return client.Do(req)
}
