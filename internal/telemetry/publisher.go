package telemetry

import (
	"context"
	"log/slog"
	"time"

	"github.com/JennGllg/mon-kado/internal/logging"
)

// SnapshotStore is the publishing boundary for private process measurements.
type SnapshotStore interface {
	Write(context.Context, Snapshot) error
}

// Publisher writes snapshots independently of business job execution.
type Publisher struct {
	Collector *Collector
	Store     SnapshotStore
	Logger    *slog.Logger
}

// Start publishes on an owned scheduler and returns a stop function that waits for shutdown.
func (p Publisher) Start(ctx context.Context, interval time.Duration) func() {
	ctx, cancel := context.WithCancel(ctx)
	ticks := time.NewTicker(interval)
	done := make(chan struct{})
	go func() { defer close(done); p.Run(ctx, ticks.C) }()
	return func() { cancel(); ticks.Stop(); <-done }
}

// Run publishes immediately and on caller-owned ticks until cancellation or scheduler closure.
func (p Publisher) Run(ctx context.Context, ticks <-chan time.Time) {
	for ctx.Err() == nil {
		if err := p.Store.Write(ctx, p.Collector.Capture()); err != nil && ctx.Err() == nil {
			logging.ProcessLogger(p.Logger).ErrorContext(ctx, "Telemetry snapshot failed", "EventId", logging.TelemetrySnapshotFailed)
		}
		select {
		case <-ctx.Done():
			return
		case _, open := <-ticks:
			if !open {
				return
			}
		}
	}
}
