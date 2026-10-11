// Package importer extracts best-effort merchant previews through a public-only HTTP connector.
package importer

import (
	"bytes"
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"golang.org/x/net/html/charset"
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

// Client owns the resolver and dialer used for every redirect and image request.
type Client struct {
	Catalog          Catalog
	Resolver         Resolver
	Dialer           Dialer
	Timeout          time.Duration
	MaximumRedirects int
	MaximumHTMLBytes int
}

// New creates an isolated client that ignores process proxy settings.
func New() *Client {
	return &Client{Resolver: net.DefaultResolver, Dialer: &net.Dialer{Timeout: 10 * time.Second}, Timeout: 20 * time.Second, MaximumRedirects: 3, MaximumHTMLBytes: 2 * 1024 * 1024}
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

// Download validates each actual connection and manually follows bounded redirects.
func (c *Client) Download(ctx context.Context, value string, limit int64) ([]byte, *url.URL, string, error) {
	transport := &http.Transport{Proxy: nil, DisableKeepAlives: true, ResponseHeaderTimeout: c.Timeout, DialContext: func(ctx context.Context, network, address string) (net.Conn, error) {
		host, port, err := net.SplitHostPort(address)
		if err != nil {
			return nil, err
		}
		addresses, err := c.addresses(ctx, host)
		if err != nil {
			return nil, err
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
	for redirects := 0; ; redirects++ {
		u, err := webURL(value)
		if err != nil {
			return nil, nil, "", err
		}
		req, err := http.NewRequestWithContext(ctx, "GET", u.String(), nil)
		if err != nil {
			return nil, nil, "", err
		}
		req.Header.Set("User-Agent", "MonKado/1.0 (+product preview)")
		response, err := client.Do(req)
		if err != nil {
			return nil, nil, "", err
		}
		if response.StatusCode >= 300 && response.StatusCode <= 399 {
			location, err := response.Location()
			response.Body.Close()
			if err != nil || redirects >= c.MaximumRedirects {
				return nil, nil, "", errors.New("merchant redirect limit exceeded")
			}
			value = location.String()
			continue
		}
		content, err := io.ReadAll(io.LimitReader(response.Body, limit+1))
		response.Body.Close()
		if err != nil {
			return nil, nil, "", err
		}
		if response.StatusCode < 200 || response.StatusCode >= 300 || int64(len(content)) > limit {
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
