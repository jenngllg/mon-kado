package importer

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"mime"
	"net"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/config"
	"golang.org/x/net/html/charset"
)

const localCloudflareEndpoint = "http://127.0.0.1:8787/preview"
const localCloudflareBudget = 2 * time.Second

// LocalCloudflare provides authenticated relay previews for public merchants.
// The default constructor is loopback-only; permanent origins require explicit managed-provider configuration.
type LocalCloudflare struct {
	validator    localURLValidator
	maximumBytes int
	key          string
	http         *http.Client
	wait         RetryWait
	origin       string
	request      providerRequest
}

// NewCloudflare connects to the dedicated permanent Worker with a DNS-pinned provider transport.
// Worker merchant connections remain managed and require the separately authorized network exception.
func NewCloudflare(client *Client, origin, key string) *LocalCloudflare {
	provider := NewLocalCloudflare(client, key)
	provider.origin = origin
	// The relay's HTTP/2 path can produce upstream throttling for otherwise
	// available documents. Select the compatible protocol before the request;
	// do not retry a refusal or change any DNS, TLS or credential boundary.
	provider.request = func(ctx context.Context, target *url.URL, method string, body io.Reader, headers http.Header) (*http.Response, error) {
		return client.requestWithProtocol(ctx, target, method, body, headers, false)
	}
	return provider
}

func (c *LocalCloudflare) relayRequest(ctx context.Context, route string, body []byte) (*http.Response, error) {
	headers := http.Header{"Content-Type": {"application/json"}, "Authorization": {"Bearer " + c.key}}
	if c.request != nil {
		origin, err := config.NormalizeCloudflareRelayOrigin(c.origin)
		if err != nil {
			return nil, err
		}
		endpoint, _ := url.Parse(origin + route)
		return c.request(ctx, endpoint, http.MethodPost, bytes.NewReader(body), headers)
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodPost, "http://127.0.0.1:8787"+route, bytes.NewReader(body))
	if err != nil {
		return nil, err
	}
	request.GetBody = nil
	request.Header = headers
	return c.http.Do(request)
}

type localURLValidator interface {
	Validate(context.Context, string) error
}

// NewLocalCloudflare creates a local-only connector with no configurable network authority.
// The API composition root must enforce the local environment before installing it.
func NewLocalCloudflare(client *Client, key string) *LocalCloudflare {
	return &LocalCloudflare{validator: client, maximumBytes: client.MaximumHTMLBytes, key: key, wait: client.Wait, http: &http.Client{
		Transport:     &http.Transport{Proxy: nil, MaxConnsPerHost: 4, MaxIdleConnsPerHost: 2, ResponseHeaderTimeout: localCloudflareBudget},
		Timeout:       localCloudflareBudget,
		CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse },
	}}
}

func cloudflareURL(target *url.URL) bool {
	if target == nil || target.User != nil || target.Scheme != "https" && target.Scheme != "http" {
		return false
	}
	if port := target.Port(); port != "" && !(target.Scheme == "https" && port == "443") && !(target.Scheme == "http" && port == "80") {
		return false
	}
	host := strings.ToLower(target.Hostname())
	if net.ParseIP(host) != nil || !strings.Contains(host, ".") || strings.HasSuffix(host, ".") || strings.ContainsAny(host, ":\\% \t\r\n") {
		return false
	}
	for _, suffix := range []string{".localhost", ".local", ".internal", ".invalid", ".test", ".example", ".onion", ".home.arpa"} {
		if strings.HasSuffix(host, suffix) {
			return false
		}
	}
	return true
}

// CanHandle admits public web merchants without a hostname allowlist.
// Get validates public DNS before contacting the relay.
func (c *LocalCloudflare) CanHandle(target *url.URL) bool {
	return cloudflareURL(target)
}

// Get uses only the bounded authenticated relay; merchant page failures remain explicit.
func (c *LocalCloudflare) Get(ctx context.Context, target *url.URL) (Metadata, error) {
	if ctx.Err() != nil {
		return Metadata{}, ctx.Err()
	}
	if target == nil || !c.CanHandle(target) {
		return Metadata{}, rejected()
	}
	// Revalidate the complete public address set before any relay operation.
	if err := c.validator.Validate(ctx, target.String()); err != nil {
		return Metadata{}, err
	}
	edgeContext, cancel := context.WithTimeout(ctx, localCloudflareBudget)
	defer cancel()
	metadata, err := c.edge(edgeContext, target)
	if ctx.Err() != nil {
		return Metadata{}, ctx.Err()
	}
	return metadata, err
}

func (c *LocalCloudflare) maximum() int {
	if c.maximumBytes > 0 {
		return c.maximumBytes
	}
	return config.MaximumImportHTMLBytes
}

func (c *LocalCloudflare) edge(ctx context.Context, target *url.URL) (Metadata, error) {
	budget, cancel := context.WithTimeout(ctx, localCloudflareBudget)
	defer cancel()
	wait := c.wait
	if wait == nil {
		wait = waitForRetry
	}
	for attempt := 0; ; attempt++ {
		metadata, err := c.edgeAttempt(budget, target)
		if err == nil || attempt == 1 || budget.Err() != nil {
			return metadata, err
		}
		delay, retry := retryableDownload(err)
		deadline, _ := budget.Deadline()
		delay = max(delay, firstRetryDelay)
		if !retry || time.Until(deadline) <= delay {
			return metadata, err
		}
		if err := wait(budget, delay); err != nil {
			return Metadata{}, err
		}
	}
}

func (c *LocalCloudflare) edgeAttempt(ctx context.Context, target *url.URL) (Metadata, error) {
	clean := *target
	clean.Fragment = ""
	clean.RawFragment = ""
	// Merchants commonly distribute old HTTP links; the edge path always uses HTTPS.
	clean.Scheme = "https"
	clean.Host = strings.ToLower(clean.Hostname())
	body, err := json.Marshal(struct {
		URL string `json:"url"`
	}{clean.String()})
	if err != nil {
		return Metadata{}, errors.New("development preview request is unavailable")
	}
	response, err := c.relayRequest(ctx, "/preview", body)
	if err != nil {
		if _, retry := retryableDownload(err); retry {
			return Metadata{}, temporaryMerchantError{}
		}
		return Metadata{}, errors.New("development preview relay is unavailable")
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusOK {
		status := response.StatusCode
		if origin := response.Header.Get("X-MonKado-Origin-Status"); origin != "" {
			parsed, err := strconv.Atoi(origin)
			if err != nil || parsed < 100 || parsed > 599 {
				return Metadata{}, errors.New("development preview relay is unavailable")
			}
			status = parsed
		}
		return Metadata{}, merchantStatusError(status, response.Header.Get("X-MonKado-Origin-Retry-After"))
	}
	media, parameters, _ := mime.ParseMediaType(response.Header.Get("Content-Type"))
	if media != "text/html" && media != "application/xhtml+xml" {
		return Metadata{}, errors.New("development preview media is unavailable")
	}
	maximum := int64(c.maximum())
	content, err := io.ReadAll(io.LimitReader(response.Body, maximum+1))
	if err != nil {
		if _, retry := retryableDownload(err); retry {
			return Metadata{}, temporaryMerchantError{}
		}
		return Metadata{}, errors.New("development preview body is unavailable")
	}
	if int64(len(content)) > maximum {
		return Metadata{}, errors.New("development preview body is unavailable")
	}
	if label := parameters["charset"]; label != "" {
		reader, err := charset.NewReaderLabel(label, bytes.NewReader(content))
		if err != nil {
			return Metadata{}, errors.New("development preview charset is unavailable")
		}
		content, err = io.ReadAll(io.LimitReader(reader, 3*maximum+1))
		if err != nil || int64(len(content)) > 3*maximum {
			return Metadata{}, errors.New("development preview text is unavailable")
		}
	}
	if c.request != nil {
		if err := c.validator.Validate(ctx, clean.String()); err != nil {
			return Metadata{}, err
		}
	}
	return Extract(content, &clean), nil
}
