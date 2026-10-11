package admission_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/admission"
)

type lease struct {
	release  func()
	accepted bool
	err      error
}

func TestAcquire_WhenQueueIsFull_PreservesFIFOAndRejectsWithoutWaiting(t *testing.T) {
	// Arrange
	enqueued := make(chan context.Context, 2)
	limiter := admission.New(func(ctx context.Context, duration time.Duration) (context.Context, context.CancelFunc) {
		if duration != 5*time.Second {
			t.Error("admission budget differs")
		}
		enqueued <- ctx
		return context.WithCancel(ctx)
	})
	active, ok, err := limiter.Acquire(t.Context())
	if err != nil || !ok {
		t.Fatal("first admission failed")
	}
	first, second := make(chan lease, 1), make(chan lease, 1)
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	go func() { release, accepted, err := limiter.Acquire(ctx); first <- lease{release, accepted, err} }()
	if <-enqueued != ctx {
		t.Fatal("caller context replaced")
	}
	go func() {
		release, accepted, err := limiter.Acquire(t.Context())
		second <- lease{release, accepted, err}
	}()
	<-enqueued
	// Act
	_, accepted, err := limiter.Acquire(t.Context())
	active()
	firstLease := <-first
	// Assert
	if accepted || err != nil || !firstLease.accepted || firstLease.err != nil {
		t.Fatal("full queue or first grant differs")
	}
	select {
	case <-second:
		t.Fatal("FIFO waiter overtook first lease")
	default:
	}
	firstLease.release()
	firstLease.release()
	secondLease := <-second
	if !secondLease.accepted || secondLease.err != nil {
		t.Fatal("second waiter failed")
	}
	secondLease.release()
	release, accepted, err := limiter.Acquire(t.Context())
	if !accepted || err != nil {
		t.Fatal("capacity leaked")
	}
	release()
}

func TestAcquire_WhenCallerCancelsOrBudgetExpires_ReleasesQueueCapacity(t *testing.T) {
	// Arrange
	queued := make(chan context.CancelFunc, 1)
	limiter := admission.New(func(ctx context.Context, _ time.Duration) (context.Context, context.CancelFunc) {
		budget, cancel := context.WithCancel(ctx)
		queued <- cancel
		return budget, cancel
	})
	active, _, _ := limiter.Acquire(t.Context())
	defer active()
	ctx, cancel := context.WithCancel(t.Context())
	result := make(chan lease, 1)
	go func() { release, accepted, err := limiter.Acquire(ctx); result <- lease{release, accepted, err} }()
	<-queued
	// Act
	cancel()
	cancelled := <-result
	// Assert
	if cancelled.accepted || !errors.Is(cancelled.err, context.Canceled) {
		t.Fatal("caller cancellation lost")
	}
	go func() {
		release, accepted, err := limiter.Acquire(t.Context())
		result <- lease{release, accepted, err}
	}()
	budgetExpired := <-queued
	budgetExpired()
	expired := <-result
	if expired.accepted || expired.err != nil {
		t.Fatal("expired admission budget was not a rejection")
	}
	_, accepted, err := limiter.Acquire(ctx)
	if accepted || !errors.Is(err, context.Canceled) {
		t.Fatal("cancelled caller admitted")
	}
}
