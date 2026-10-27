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
	"sync"
	"time"
)

const localFirecrawlEndpoint = "https://api.firecrawl.dev/v2/scrape"
const localFirecrawlBudget = 6 * time.Second
const firecrawlStartInterval = 6100 * time.Millisecond
const firecrawlImageReserve = time.Second

type providerRequest func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error)

type firecrawlRequest struct {
	URL                 string            `json:"url"`
	Formats             []string          `json:"formats"`
	OnlyMainContent     bool              `json:"onlyMainContent"`
	Proxy               string            `json:"proxy"`
	Timeout             int64             `json:"timeout"`
	SkipTLSVerification bool              `json:"skipTlsVerification"`
	Location            firecrawlLocation `json:"location"`
}

type firecrawlLocation struct {
	Country   string   `json:"country"`
	Languages []string `json:"languages"`
}

// LocalFirecrawl supplies a single authenticated fallback for development previews.
// The API composition root must reject production activation; the remote renderer
// controls its merchant connections and cannot enforce MonKado's IP pinning.
type LocalFirecrawl struct {
	validator               localURLValidator
	request                 providerRequest
	key                     string
	maximumBytes            int
	now                     func() time.Time
	wait                    RetryWait
	slots                   chan struct{}
	mu                      sync.Mutex
	nextStart, blockedUntil time.Time
	disabled                bool
	product                 bool
	pageBudget              time.Duration
}

// NewLocalFirecrawlProduct enables deterministic product fields alongside raw HTML.
// It shares the same local-only transport, admission and monetary validation.
func NewLocalFirecrawlProduct(client *Client, key string, now func() time.Time) *LocalFirecrawl {
	provider := NewLocalFirecrawl(client, key, now)
	provider.product = true
	return provider
}

// NewLocalFirecrawl creates a local-only provider with a fixed API authority.
// Its clock is injected so quota and admission behavior can be tested deterministically.
func NewLocalFirecrawl(client *Client, key string, now func() time.Time) *LocalFirecrawl {
	return &LocalFirecrawl{validator: client, request: client.requestWithHeaders, key: key,
		maximumBytes: client.MaximumHTMLBytes, now: now, wait: waitForRetry, slots: make(chan struct{}, 2),
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
	if duration < time.Second {
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
	if remaining < time.Second {
		return Metadata{}, errors.New("development fallback budget is unavailable")
	}
	formats := []string{"rawHtml"}
	if includeProduct {
		formats = append(formats, "product")
	}
	body, _ := json.Marshal(firecrawlRequest{
		URL: clean.String(), Formats: formats, OnlyMainContent: false, Proxy: "auto",
		Timeout: max(1000, remaining.Milliseconds()-500), SkipTLSVerification: false,
		// The app imports EUR amounts; the provider otherwise defaults to the US market.
		Location: firecrawlLocation{Country: "FR", Languages: []string{"fr-FR", "fr"}},
	})
	maximum := c.maximumBytes
	if maximum <= 0 {
		maximum = 4 * 1024 * 1024
	}
	// JSON escaping can expand raw HTML while its decoded size remains bounded.
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
				Status    int    `json:"statusCode"`
				SourceURL string `json:"sourceURL"`
				URL       string `json:"url"`
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
	if (base.Path == "" || base.Path == "/") && clean.Path != "" && clean.Path != "/" {
		return Metadata{}, nil
	}
	if includeProduct {
		return ExtractFirecrawlProduct([]byte(envelope.Data.RawHTML), envelope.Data.Product, base, &clean), nil
	}
	return Extract([]byte(envelope.Data.RawHTML), base), nil
}

func (c *LocalFirecrawl) post(ctx context.Context, body []byte, maximum int) ([]byte, error) {
	endpoint, _ := url.Parse(localFirecrawlEndpoint)
	headers := make(http.Header)
	headers.Set("Content-Type", "application/json")
	headers.Set("Accept", "application/json")
	headers.Set("Authorization", "Bearer "+c.key)
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
	c.mu.Lock()
	now := c.now()
	if c.disabled || now.Before(c.blockedUntil) {
		c.mu.Unlock()
		return errors.New("development fallback admission is unavailable")
	}
	start := maxTime(c.nextStart, now)
	c.nextStart = start.Add(firecrawlStartInterval)
	c.mu.Unlock()
	if delay := start.Sub(now); delay > 0 {
		if err := c.wait(ctx, delay); err != nil {
			return err
		}
	}
	// Another admitted request may exhaust the account while this one waits.
	c.mu.Lock()
	blocked := c.disabled || c.now().Before(c.blockedUntil)
	c.mu.Unlock()
	if blocked {
		return errors.New("development fallback admission is unavailable")
	}
	return ctx.Err()
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
