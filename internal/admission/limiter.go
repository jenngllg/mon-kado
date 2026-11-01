// Package admission bounds expensive request pipelines before body buffering.
package admission

import (
	"context"
	"sync"
	"time"
)

const maximumQueued = 2
const maximumWait = 5 * time.Second

// WaitBudget creates a cancellable admission deadline using the caller's context.
type WaitBudget func(context.Context, time.Duration) (context.Context, context.CancelFunc)

// Limiter serializes image work with two FIFO waiters and a five-second admission budget.
type Limiter struct {
	mu     sync.Mutex
	active bool
	queue  []chan struct{}
	budget WaitBudget
}

// New composes admission with a controllable deadline; nil uses the standard clock.
func New(budget WaitBudget) *Limiter {
	if budget == nil {
		budget = context.WithTimeout
	}
	return &Limiter{budget: budget}
}

// Acquire returns a release function, an admission result, and caller cancellation when applicable.
func (l *Limiter) Acquire(ctx context.Context) (func(), bool, error) {
	if err := ctx.Err(); err != nil {
		return nil, false, err
	}
	l.mu.Lock()
	if !l.active {
		l.active = true
		l.mu.Unlock()
		return l.release(), true, nil
	}
	if len(l.queue) >= maximumQueued {
		l.mu.Unlock()
		return nil, false, nil
	}
	ready := make(chan struct{})
	l.queue = append(l.queue, ready)
	l.mu.Unlock()
	budget, cancel := l.budget(ctx, maximumWait)
	defer cancel()
	select {
	case <-ready:
		return l.release(), true, nil
	case <-budget.Done():
		l.mu.Lock()
		found := false
		for index, queued := range l.queue {
			if queued == ready {
				l.queue = append(l.queue[:index], l.queue[index+1:]...)
				found = true
				break
			}
		}
		// A grant racing cancellation still owns a slot and must release it.
		if !found {
			l.advance()
		}
		l.mu.Unlock()
		return nil, false, ctx.Err()
	}
}

func (l *Limiter) release() func() {
	var once sync.Once
	return func() { once.Do(func() { l.mu.Lock(); defer l.mu.Unlock(); l.advance() }) }
}
func (l *Limiter) advance() {
	if len(l.queue) == 0 {
		l.active = false
		return
	}
	ready := l.queue[0]
	l.queue = l.queue[1:]
	close(ready)
}
