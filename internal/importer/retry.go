package importer

import (
	"context"
	"errors"
	"io"
	"net"
	"net/http"
	"net/url"
	"strconv"
	"syscall"
	"time"
)

const maximumDownloadRetries = 2
const firstRetryDelay = 250 * time.Millisecond

// RetryWait provides a cancellable backoff, replaceable for deterministic transport tests.
type RetryWait func(context.Context, time.Duration) error

type temporaryMerchantError struct{ minimumDelay time.Duration }

func (temporaryMerchantError) Error() string { return "merchant is temporarily unavailable" }

func merchantStatusError(status int, retryAfter string) error {
	if status != http.StatusRequestTimeout && status != http.StatusInternalServerError && status != http.StatusBadGateway && status != http.StatusServiceUnavailable && status != http.StatusGatewayTimeout {
		return errors.New("merchant response is unavailable")
	}
	var delay time.Duration
	if retryAfter != "" {
		seconds, err := strconv.ParseUint(retryAfter, 10, 32)
		if err != nil {
			// Do not guess an absolute Retry-After date or bypass an unrecognized server instruction.
			return errors.New("merchant requires a later request")
		}
		delay = time.Duration(seconds) * time.Second
	}
	return temporaryMerchantError{minimumDelay: delay}
}

func retryableDownload(err error) (time.Duration, bool) {
	var status temporaryMerchantError
	if errors.As(err, &status) {
		return status.minimumDelay, true
	}
	var network net.Error
	if errors.As(err, &network) && (network.Timeout() || network.Temporary()) {
		return 0, true
	}
	return 0, errors.Is(err, io.EOF) || errors.Is(err, io.ErrUnexpectedEOF) || errors.Is(err, syscall.ECONNRESET) || errors.Is(err, syscall.ECONNREFUSED)
}

// Download retries only transient GET failures, revalidating every connection and redirect.
// All attempts and backoffs share one timeout and the caller's cancellation budget.
func (c *Client) Download(ctx context.Context, value string, limit int64) ([]byte, *url.URL, string, error) {
	return c.download(ctx, value, limit, "")
}

func (c *Client) download(ctx context.Context, value string, limit int64, accept string) ([]byte, *url.URL, string, error) {
	budget, cancel := context.WithTimeout(ctx, c.Timeout)
	defer cancel()
	wait := c.Wait
	if wait == nil {
		wait = waitForRetry
	}
	for attempt := 0; ; attempt++ {
		if err := budget.Err(); err != nil {
			return nil, nil, "", err
		}
		content, final, media, err := c.downloadAttempt(budget, value, limit, accept)
		if err == nil {
			return content, final, media, nil
		}
		minimum, retry := retryableDownload(err)
		if !retry || attempt == maximumDownloadRetries {
			return nil, nil, "", err
		}
		delay := max(firstRetryDelay<<attempt, minimum)
		deadline, _ := budget.Deadline()
		if time.Until(deadline) <= delay {
			return nil, nil, "", err
		}
		if err := wait(budget, delay); err != nil {
			return nil, nil, "", err
		}
	}
}

func waitForRetry(ctx context.Context, delay time.Duration) error {
	timer := time.NewTimer(delay)
	defer timer.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-timer.C:
		return nil
	}
}
