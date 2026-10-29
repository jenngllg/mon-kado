package importer

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"mime"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"sync"
	"time"
)

const localFirecrawlEndpoint = "https://api.firecrawl.dev/v2/scrape"
const localFirecrawlBudget = 6 * time.Second
const firecrawlStartInterval = 6100 * time.Millisecond
const firecrawlImageReserve = time.Second
const firecrawlMinimumTransferBudget = time.Second

// Leave room for the local preview cache and the bounded provider transfer
// within the user-selected maximum age of 48 hours.
const firecrawlPageMaxAge = maximumPriceFreshness - previewCacheLifetime - providerFreshnessSafety
const keylessWindow = time.Minute
const keylessWindowLimit = 10

var (
	errKeylessQuota     = errors.New("keyless fallback quota is unavailable")
	errKeylessBusy      = errors.New("keyless fallback pacing is unavailable")
	errKeylessTransient = errors.New("keyless fallback failed transiently")
)

type providerRequest func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error)

type firecrawlRequest struct {
	URL                 string            `json:"url"`
	Formats             []string          `json:"formats"`
	OnlyMainContent     bool              `json:"onlyMainContent"`
	Proxy               string            `json:"proxy"`
	Timeout             int64             `json:"timeout"`
	MaxAge              int64             `json:"maxAge,omitempty"`
	SkipTLSVerification bool              `json:"skipTlsVerification"`
	Location            firecrawlLocation `json:"location"`
}

type firecrawlLocation struct {
	Country   string   `json:"country"`
	Languages []string `json:"languages"`
}

// LocalFirecrawl supplies one authenticated managed fallback for merchant previews.
// The API composition root requires explicit configuration; the remote renderer
// controls its merchant connections and cannot enforce MonKado's IP pinning.
type LocalFirecrawl struct {
	validator               localURLValidator
	request                 providerRequest
	key                     string
	maximumBytes            int
	now                     func() time.Time
	wait                    RetryWait
	slots                   chan struct{}
	admission               chan struct{}
	mu                      sync.Mutex
	nextStart, blockedUntil time.Time
	rollingStarts           []time.Time
	localRolling            bool
	disabled                bool
	product                 bool
	pageBudget              time.Duration
}

// NewLocalFirecrawlProduct enables deterministic product fields alongside raw HTML.
// It shares the same guarded transport, admission and monetary validation.
func NewLocalFirecrawlProduct(client *Client, key string, now func() time.Time) *LocalFirecrawl {
	provider := NewLocalFirecrawl(client, key, now)
	provider.product = true
	return provider
}

// NewLocalFirecrawlRolling enables a bounded rolling admission window for a
// local experiment. Page and image requests share the same account window.
func NewLocalFirecrawlRolling(client *Client, key string, now func() time.Time, product bool) *LocalFirecrawl {
	provider := NewLocalFirecrawl(client, key, now)
	provider.localRolling, provider.product = true, product
	return provider
}

// NewLocalFirecrawl creates a managed provider with a fixed API authority.
// Its clock is injected so quota and admission behavior can be tested deterministically.
func NewLocalFirecrawl(client *Client, key string, now func() time.Time) *LocalFirecrawl {
	return &LocalFirecrawl{validator: client, request: client.requestWithHeaders, key: key,
		maximumBytes: client.MaximumHTMLBytes, now: now, wait: waitForRetry, slots: make(chan struct{}, 2), admission: make(chan struct{}, 1),
		pageBudget: min(localFirecrawlBudget, client.Timeout-firecrawlImageReserve)}
}

// CanHandle admits the same public web authorities as the primary development relay.
func (c *LocalFirecrawl) CanHandle(target *url.URL) bool { return cloudflareURL(target) }

// Get retrieves bounded raw HTML once and uses the shared passive parser.
// No provider error body, merchant cookie or credential is forwarded or logged.
func (c *LocalFirecrawl) Get(ctx context.Context, target *url.URL) (Metadata, error) {
	return c.get(ctx, target, c.product)
}

// GetHTML retrieves the rendered document without optional provider enrichment.
// It shares the same admission, deadline and single-POST boundary as Get.
func (c *LocalFirecrawl) GetHTML(ctx context.Context, target *url.URL) (Metadata, error) {
	return c.get(ctx, target, false)
}

func (c *LocalFirecrawl) get(ctx context.Context, target *url.URL, includeProduct bool) (Metadata, error) {
	if err := ctx.Err(); err != nil {
		return Metadata{}, err
	}
	if !c.CanHandle(target) {
		return Metadata{}, rejected()
	}
	clean := *target
	clean.Scheme = "https"
	clean.Host = clean.Hostname()
	clean.Fragment, clean.RawFragment = "", ""
	if err := c.validator.Validate(ctx, clean.String()); err != nil {
		return Metadata{}, err
	}
	duration := min(localFirecrawlBudget, c.pageBudget)
	if deadline, ok := ctx.Deadline(); ok {
		duration = min(duration, time.Until(deadline)-firecrawlImageReserve)
	}
	if duration < firecrawlMinimumTransferBudget {
		return Metadata{}, errors.New("development fallback budget is unavailable")
	}
	budget, cancel := context.WithTimeout(ctx, duration)
	defer cancel()
	select {
	case c.slots <- struct{}{}:
		defer func() { <-c.slots }()
	case <-budget.Done():
		return Metadata{}, budget.Err()
	}
	if err := c.admit(budget); err != nil {
		return Metadata{}, err
	}
	deadline, _ := budget.Deadline()
	remaining := time.Until(deadline)
	if remaining < firecrawlMinimumTransferBudget {
		return Metadata{}, errors.New("development fallback budget is unavailable")
	}
	formats := []string{"rawHtml"}
	if includeProduct {
		formats = append(formats, "product")
	}
	body, _ := json.Marshal(firecrawlRequest{
		URL: clean.String(), Formats: formats, OnlyMainContent: false, Proxy: "auto",
		Timeout: max(1000, remaining.Milliseconds()-500), MaxAge: firecrawlPageMaxAge.Milliseconds(), SkipTLSVerification: false,
		// The app imports EUR amounts; the provider otherwise defaults to the US market.
		Location: firecrawlLocation{Country: "FR", Languages: []string{"fr-FR", "fr"}},
	})
	maximum := c.maximumBytes
	if maximum <= 0 {
		maximum = 4 * 1024 * 1024
	}
	// JSON escaping can expand raw HTML while its decoded size remains bounded.
	started := c.now().UTC()
	content, err := c.post(budget, body, 6*maximum)
	if err != nil {
		return Metadata{}, err
	}
	var envelope struct {
		Success bool `json:"success"`
		Data    struct {
			RawHTML  string          `json:"rawHtml"`
			Product  json.RawMessage `json:"product"`
			Metadata struct {
				Status     int    `json:"statusCode"`
				SourceURL  string `json:"sourceURL"`
				URL        string `json:"url"`
				CacheState string `json:"cacheState"`
				CachedAt   string `json:"cachedAt"`
			} `json:"metadata"`
		} `json:"data"`
	}
	if json.Unmarshal(content, &envelope) != nil || !envelope.Success || envelope.Data.Metadata.Status != 200 || len(envelope.Data.RawHTML) > maximum || envelope.Data.RawHTML == "" {
		return Metadata{}, errors.New("development fallback document is unavailable")
	}
	base := &clean
	for _, value := range []string{envelope.Data.Metadata.SourceURL, envelope.Data.Metadata.URL} {
		if value == "" {
			continue
		}
		final, err := url.Parse(value)
		if err != nil || !c.CanHandle(final) || final.Scheme != "https" {
			return Metadata{}, rejected()
		}
		if err := c.validator.Validate(budget, final.String()); err != nil {
			return Metadata{}, err
		}
		base = final
	}
	if err := budget.Err(); err != nil {
		return Metadata{}, err
	}
	// The provider may follow a retired product URL to its storefront. Its title
	// is not the requested item, even when the storefront responds with HTTP 200.
	if isNonProductRedirect(&clean, base) {
		return Metadata{}, nil
	}
	var result Metadata
	if includeProduct {
		result = ExtractFirecrawlProduct([]byte(envelope.Data.RawHTML), envelope.Data.Product, base, &clean)
	} else {
		result = Extract([]byte(envelope.Data.RawHTML), base)
	}
	switch envelope.Data.Metadata.CacheState {
	case "miss":
		result.priceFreshUntil = started.Add(maximumPriceFreshness - providerFreshnessSafety)
	case "hit":
		cachedAt, parseErr := time.Parse(time.RFC3339Nano, envelope.Data.Metadata.CachedAt)
		if parseErr == nil && !cachedAt.After(started) {
			result.priceFreshUntil = cachedAt.UTC().Add(maximumPriceFreshness - providerFreshnessSafety)
			if !result.priceFreshUntil.After(started) {
				return Metadata{}, errors.New("development fallback cache is stale")
			}
		}
	}
	return result, nil
}

// A provider may follow a retired product to a successful HTML error page.
// Restrict this guard to explicit destination names and actual path changes so
// ordinary product redirects continue through the normal extraction checks.
func isNonProductRedirect(requested, final *url.URL) bool {
	if requested.Path == final.Path {
		return false
	}
	path := strings.Trim(strings.ToLower(final.Path), "/")
	if path == "" {
		return requested.Path != "" && requested.Path != "/"
	}
	parts := strings.Split(path, "/")
	last := strings.TrimSuffix(parts[len(parts)-1], ".html")
	switch last {
	case "404", "not-found", "page-not-found", "product-not-found", "page-unavailable", "product-unavailable", "page-indisponible", "produit-indisponible", "page-introuvable", "produit-introuvable":
		return true
	default:
		return false
	}
}

func (c *LocalFirecrawl) post(ctx context.Context, body []byte, maximum int) ([]byte, error) {
	endpoint, _ := url.Parse(localFirecrawlEndpoint)
	headers := make(http.Header)
	headers.Set("Content-Type", "application/json")
	headers.Set("Accept", "application/json")
	if c.key != "" {
		headers.Set("Authorization", "Bearer "+c.key)
	}
	response, err := c.request(ctx, endpoint, http.MethodPost, bytes.NewReader(body), headers)
	if err != nil {
		if ctx.Err() != nil {
			return nil, ctx.Err()
		}
		return nil, errors.New("development fallback is unavailable")
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusOK {
		c.block(response.StatusCode, response.Header.Get("Retry-After"))
		if c.key == "" && response.StatusCode == http.StatusTooManyRequests {
			return nil, errKeylessQuota
		}
		if c.key == "" && (response.StatusCode == http.StatusRequestTimeout || response.StatusCode >= 500 && response.StatusCode <= 599) {
			return nil, errKeylessTransient
		}
		return nil, errors.New("development fallback is unavailable")
	}
	media, _, _ := mime.ParseMediaType(response.Header.Get("Content-Type"))
	if media != "application/json" {
		return nil, errors.New("development fallback media is unavailable")
	}
	content, err := io.ReadAll(io.LimitReader(response.Body, int64(maximum)+1))
	if err != nil || len(content) > maximum {
		return nil, errors.New("development fallback body is unavailable")
	}
	return content, nil
}

func (c *LocalFirecrawl) admit(ctx context.Context) error {
	select {
	case c.admission <- struct{}{}:
		defer func() { <-c.admission }()
	case <-ctx.Done():
		return ctx.Err()
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	c.mu.Lock()
	now := c.now()
	if c.disabled || now.Before(c.blockedUntil) {
		quota := c.key == "" && now.Before(c.blockedUntil)
		c.mu.Unlock()
		if quota {
			return errKeylessQuota
		}
		return errors.New("development fallback admission is unavailable")
	}
	start := maxTime(c.nextStart, now)
	if c.key == "" || c.localRolling {
		c.pruneRollingStarts(now)
		start = now
		if len(c.rollingStarts) >= keylessWindowLimit {
			start = c.rollingStarts[0].Add(keylessWindow)
		}
	}
	c.mu.Unlock()
	if delay := start.Sub(now); delay > 0 {
		if c.key == "" || c.localRolling {
			if deadline, ok := ctx.Deadline(); ok && time.Until(deadline) < delay+2*firecrawlMinimumTransferBudget {
				if c.key == "" {
					return errKeylessBusy
				}
				return errors.New("development fallback pacing is unavailable")
			}
		}
		if err := c.wait(ctx, delay); err != nil {
			return err
		}
	}
	// An in-flight request may exhaust the account while this one waits.
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.disabled || c.now().Before(c.blockedUntil) {
		if c.key == "" && c.now().Before(c.blockedUntil) {
			return errKeylessQuota
		}
		return errors.New("development fallback admission is unavailable")
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	if deadline, ok := ctx.Deadline(); ok && time.Until(deadline) < firecrawlMinimumTransferBudget {
		return errors.New("development fallback budget is unavailable")
	}
	// Commit pacing only for work that can start. Canceled waiters never reserve
	// future slots and cannot accumulate delays for unrelated previews.
	if c.key == "" || c.localRolling {
		c.pruneRollingStarts(c.now())
		if len(c.rollingStarts) >= keylessWindowLimit {
			if c.key == "" {
				return errKeylessBusy
			}
			return errors.New("development fallback pacing is unavailable")
		}
		c.rollingStarts = append(c.rollingStarts, c.now())
	} else {
		c.nextStart = maxTime(start, c.now()).Add(firecrawlStartInterval)
	}
	return nil
}

// pruneRollingStarts is called only while holding c.mu.
func (c *LocalFirecrawl) pruneRollingStarts(now time.Time) {
	for len(c.rollingStarts) > 0 && !c.rollingStarts[0].Add(keylessWindow).After(now) {
		c.rollingStarts = c.rollingStarts[1:]
	}
}

func maxTime(a, b time.Time) time.Time {
	if a.After(b) {
		return a
	}
	return b
}

func (c *LocalFirecrawl) block(status int, retryAfter string) {
	c.mu.Lock()
	defer c.mu.Unlock()
	switch status {
	case 401, 402, 403:
		c.disabled = true
	case 429:
		now := c.now()
		c.blockedUntil = now.Add(time.Minute)
		if seconds, err := strconv.ParseUint(retryAfter, 10, 32); err == nil {
			c.blockedUntil = now.Add(time.Duration(seconds) * time.Second)
		} else if date, err := http.ParseTime(retryAfter); err == nil && date.After(now) {
			c.blockedUntil = date
		}
	}
}
