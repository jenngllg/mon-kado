package providers

import (
	"errors"
	"github.com/JennGllg/mon-kado/internal/application"
	"sync"
	"time"
)

type circuit struct {
	mu       sync.Mutex
	failures int
	until    time.Time
}

func (c *circuit) allow(now time.Time) error {
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.until.After(now) {
		return &application.DeliveryError{Category: "circuitOpen", Retryable: true, RetryAfter: c.until.Sub(now)}
	}
	if !c.until.IsZero() {
		c.until = time.Time{}
		c.failures = 0
	}
	return nil
}
func (c *circuit) record(now time.Time, err error, threshold int, duration time.Duration) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if err == nil {
		c.failures = 0
		c.until = time.Time{}
		return
	}
	var failure *application.DeliveryError
	if !errors.As(err, &failure) || !failure.Retryable || failure.Category == "circuitOpen" {
		return
	}
	c.failures++
	if c.failures >= threshold {
		c.until = now.Add(duration)
	}
}
