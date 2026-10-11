package worker

import (
	"context"
	"errors"
	"time"

	"github.com/JennGllg/mon-kado/internal/telemetry"
)

type job struct {
	Name       string
	Operations []telemetry.Operation
	Execute    func(context.Context) error
}

func (w *Worker) initializeTelemetry() {
	w.telemetryOnce.Do(func() {
		if w.Telemetry == nil {
			w.Telemetry = telemetry.New("worker", func() time.Time { return w.Service.Now() }, time.Now)
		}
		if w.Sender == nil {
			w.Telemetry.Disable(telemetry.AuthenticationEmailDelivery)
			w.Telemetry.Disable(telemetry.WishlistModerationEmailDelivery)
		}
	})
}

func (w *Worker) jobs() []job {
	w.initializeTelemetry()
	jobs := []job{{"exports", []telemetry.Operation{telemetry.PersonalDataExport}, w.generateExports}}
	if w.Sender != nil {
		jobs = append(jobs,
			job{"authenticationEmail", []telemetry.Operation{telemetry.AuthenticationEmailDelivery}, w.deliverAuthentication},
			job{"moderationEmail", []telemetry.Operation{telemetry.WishlistModerationEmailDelivery}, func(ctx context.Context) error { return w.deliverNotifications(ctx, false) }},
			job{"erasureEmail", []telemetry.Operation{telemetry.AccountErasureProcessing}, func(ctx context.Context) error { return w.deliverNotifications(ctx, true) }},
		)
	}
	// Recipient expiration must keep progressing even when a provider attempt is blocked.
	erasureOperations := []telemetry.Operation{}
	if w.Sender == nil {
		erasureOperations = append(erasureOperations, telemetry.AccountErasureProcessing)
	}
	jobs = append(jobs,
		job{"images", []telemetry.Operation{telemetry.GiftImageCleanup}, func(ctx context.Context) error { return errors.Join(w.cleanupImages(ctx), w.reconcileFiles(ctx)) }},
		job{"erasureExpiry", erasureOperations, w.cleanupExpiredErasure},
		job{"authenticationCleanup", []telemetry.Operation{telemetry.UnconfirmedAccountCleanup, telemetry.ExpiredAuthenticationSessionCleanup, telemetry.ExpiredMemberEmailChangeRequestCleanup, telemetry.ProcessedAuthenticationEmailCleanup, telemetry.ExpiredGuestSessionCleanup}, w.cleanupAuthentication},
	)
	return jobs
}

func (w *Worker) executeJob(ctx context.Context, job job) error {
	for _, operation := range job.Operations {
		w.Telemetry.BeginCycle(operation)
	}
	err := job.Execute(ctx)
	if ctx.Err() == nil {
		for _, operation := range job.Operations {
			w.Telemetry.CompleteCycle(operation, w.Service.Config.WorkerInterval, err == nil)
		}
	}
	return err
}
