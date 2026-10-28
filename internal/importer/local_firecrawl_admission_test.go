package importer

import (
	"context"
	"errors"
	"io"
	"net/http"
	"net/url"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

func TestLocalFirecrawl_WhenQueuedRequestCanceled_DoesNotDelayNextAvailableRequest(t *testing.T) {
	for _, image := range []bool{false, true} {
		t.Run(map[bool]string{false: "page", true: "image"}[image], func(t *testing.T) {
			// Arrange
			calls, extraWaits := 0, 0
			client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
				calls++
				if image {
					return firecrawlImageDocument("aW1hZ2U=", "image/png", "", 200), nil
				}
				return firecrawlDocument("<title>Gift</title>", "", "", 200), nil
			})
			now := client.now()
			client.now = func() time.Time { return now }
			available := now.Add(time.Second)
			client.nextStart = available
			first, cancel := context.WithCancel(t.Context())
			defer cancel()
			client.wait = func(ctx context.Context, _ time.Duration) error { cancel(); return ctx.Err() }
			target, _ := url.Parse("https://merchant.fr/product")
			invoke := func(ctx context.Context) error {
				if image {
					_, err := client.GetImage(ctx, target)
					return err
				}
				_, err := client.Get(ctx, target)
				return err
			}
			// Act
			firstError := invoke(first)
			reservedAfterCancel := client.nextStart
			now = available
			client.wait = func(context.Context, time.Duration) error {
				extraWaits++
				return errors.New("unexpected delay from canceled request")
			}
			secondError := invoke(t.Context())
			// Assert
			if !errors.Is(firstError, context.Canceled) || secondError != nil || calls != 1 || extraWaits != 0 || reservedAfterCancel != available {
				t.Fatalf("canceled request reserved a phantom slot: cancellation=%t nextRequestSucceeded=%t providerCalls=%d unexpectedWaits=%d reservationRetained=%t", errors.Is(firstError, context.Canceled), secondError == nil, calls, extraWaits, reservedAfterCancel != available)
			}
		})
	}
}

func TestLocalFirecrawl_WhenConcurrentWaitersCanceled_PreservesNextUsableSlot(t *testing.T) {
	// Arrange
	var calls atomic.Int32
	client := firecrawlFixture(t, func(context.Context, *url.URL, string, io.Reader, http.Header) (*http.Response, error) {
		calls.Add(1)
		return firecrawlDocument("<title>Gift</title>", "", "", 200), nil
	})
	now := client.now()
	client.now = func() time.Time { return now }
	available := now.Add(time.Second)
	client.nextStart = available
	waiting := make(chan struct{})
	var once sync.Once
	client.wait = func(ctx context.Context, _ time.Duration) error {
		once.Do(func() { close(waiting) })
		<-ctx.Done()
		return ctx.Err()
	}
	first, cancelFirst := context.WithCancel(t.Context())
	defer cancelFirst()
	second, cancelSecond := context.WithCancel(t.Context())
	defer cancelSecond()
	validatedSecond := make(chan struct{})
	client.validator = localDirectFake{validate: func(ctx context.Context, _ string) error {
		if ctx == second {
			close(validatedSecond)
		}
		return ctx.Err()
	}}
	target, _ := url.Parse("https://merchant.fr/product")
	firstResult, secondResult := make(chan error, 1), make(chan error, 1)
	// Act
	go func() { _, err := client.Get(first, target); firstResult <- err }()
	<-waiting
	go func() { _, err := client.Get(second, target); secondResult <- err }()
	<-validatedSecond
	cancelSecond()
	secondError := <-secondResult
	cancelFirst()
	firstError := <-firstResult
	now = available
	client.wait = func(context.Context, time.Duration) error { return errors.New("canceled waiters left a delay") }
	result, nextError := client.Get(t.Context(), target)
	// Assert
	if !errors.Is(firstError, context.Canceled) || !errors.Is(secondError, context.Canceled) || nextError != nil || calls.Load() != 1 || result.Name == nil {
		t.Fatal("canceled concurrent waiters prevented the next usable request")
	}
}
