// Package worker executes leased background work and deterministic cleanup passes.
package worker

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/logging"
	"log/slog"
	"os"
	"strings"
	"sync"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/JennGllg/mon-kado/internal/telemetry"
	"github.com/jackc/pgx/v5"
)

// Worker composes application jobs and an explicitly substituted external email provider.
type Worker struct {
	Telemetry     *telemetry.Collector
	telemetryOnce sync.Once
	Service       *application.Service
	Sender        application.EmailSender
	Logger        *slog.Logger
	tickLock      sync.Mutex
	scanLock      sync.Mutex
	directories   map[string]*os.File
}

// Run waits on context cancellation between native worker passes.
func (w *Worker) Run(ctx context.Context) error {
	defer w.Close()
	if ctx.Err() != nil {
		return nil
	}
	jobs := w.jobs()
	var running sync.WaitGroup
	for _, job := range jobs {
		running.Go(func() {
			ticker := time.NewTicker(w.Service.Config.WorkerInterval)
			defer ticker.Stop()
			for ctx.Err() == nil {
				err := w.executeJob(ctx, job)
				if err != nil && ctx.Err() == nil {
					id := security.NewID()
					w.Logger.ErrorContext(ctx, "Worker pass failed", "EventId", logging.WorkerPassFailed, "Job", job.Name, "ExceptionType", "dependency", "CorrelationId", id, "TraceId", strings.ReplaceAll(id, "-", ""))
				} else if err == nil {
					id := security.NewID()
					w.Logger.InfoContext(ctx, "Worker pass completed", "EventId", logging.WorkerPassCompleted, "Job", job.Name, "CorrelationId", id, "TraceId", strings.ReplaceAll(id, "-", ""))
				}
				select {
				case <-ctx.Done():
					return
				case <-ticker.C:
				}
			}
		})
	}
	running.Wait()
	return nil
}

// Tick executes one bounded pass, allowing tests to synchronize without polling or delays.
func (w *Worker) Tick(ctx context.Context) error {
	w.tickLock.Lock()
	defer w.tickLock.Unlock()
	for _, job := range w.jobs() {
		if err := w.executeJob(ctx, job); err != nil {
			return err
		}
	}
	return nil
}

// Close releases directory scan cursors after native worker execution stops.
func (w *Worker) Close() {
	w.tickLock.Lock()
	defer w.tickLock.Unlock()
	w.closeFiles()
}
func (w *Worker) closeFiles() {
	w.scanLock.Lock()
	defer w.scanLock.Unlock()
	w.closeFilesLocked()
}

func (w *Worker) closeFilesLocked() {
	for root, directory := range w.directories {
		directory.Close()
		delete(w.directories, root)
	}
}

func (w *Worker) deliverAuthentication(ctx context.Context) error {
	for i := 0; i < w.Service.Config.Jobs().BatchSize; i++ {
		id, lease := "", security.NewID()
		now := w.Service.Now().UTC()
		err := w.Service.Store.Pool.QueryRow(ctx, `UPDATE authentication_email_outbox SET lease_id=$2,locked_until=$3,attempt_count=attempt_count+1 WHERE id=(SELECT id FROM authentication_email_outbox WHERE processed_at IS NULL AND available_at<=$1 AND (locked_until IS NULL OR locked_until<=$1) AND attempt_count<$4 ORDER BY available_at,created_at FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING id::text`, now, lease, now.Add(w.Service.Config.Jobs().LeaseDuration), w.Service.Config.MaximumAttempts).Scan(&id)
		if errors.Is(err, pgx.ErrNoRows) {
			return nil
		}
		if err != nil {
			return err
		}
		message, err := w.Service.PrepareAuthenticationEmail(ctx, id)
		if errors.Is(err, application.ErrEmailSuperseded) {
			if _, err = w.Service.Store.Pool.Exec(ctx, `UPDATE authentication_email_outbox SET processed_at=$3,locked_until=NULL,lease_id=NULL,last_error='superseded',protected_payload=NULL WHERE id=$1 AND lease_id=$2`, id, lease, w.Service.Now().UTC()); err != nil {
				return err
			}
			continue
		}
		providerID := ""
		if err == nil {
			providerID, err = w.Sender.Send(ctx, id, message)
		}
		if ctx.Err() != nil {
			return ctx.Err()
		}
		if err == nil {
			if _, err = w.Service.Store.Pool.Exec(ctx, `UPDATE authentication_email_outbox SET processed_at=$3,locked_until=NULL,lease_id=NULL,provider_message_id=$4,last_error=NULL,protected_payload=NULL WHERE id=$1 AND lease_id=$2`, id, lease, w.Service.Now().UTC(), providerID); err != nil {
				return err
			}
			w.Logger.InfoContext(ctx, "Authentication email delivered", "EventId", logging.AuthenticationEmailDelivered, "OutboxId", id, "CorrelationId", lease, "TraceId", strings.ReplaceAll(lease, "-", ""))
			continue
		}
		failure := &application.DeliveryError{Category: "processing", Retryable: true}
		errors.As(err, &failure)
		var attempts int
		if err = w.Service.Store.Pool.QueryRow(ctx, `SELECT attempt_count FROM authentication_email_outbox WHERE id=$1 AND lease_id=$2`, id, lease).Scan(&attempts); err != nil {
			return err
		}
		delay := retryDelay(attempts, w.Service.Config.Jobs().FirstRetryDelay, w.Service.Config.Jobs().MaximumRetryDelay)
		if failure.RetryAfter > delay {
			delay = min(failure.RetryAfter, w.Service.Config.Jobs().MaximumRetryDelay)
		}
		if !failure.Retryable {
			attempts = w.Service.Config.MaximumAttempts
		}
		command, err := w.Service.Store.Pool.Exec(ctx, `UPDATE authentication_email_outbox SET available_at=$3,locked_until=NULL,lease_id=NULL,last_error=$4,attempt_count=$5::integer,processed_at=CASE WHEN $5::integer>=$6::integer THEN $7::timestamptz ELSE NULL END,protected_payload=CASE WHEN $5::integer>=$6::integer THEN NULL ELSE protected_payload END WHERE id=$1 AND lease_id=$2`, id, lease, w.Service.Now().UTC().Add(delay), failure.Category, attempts, w.Service.Config.MaximumAttempts, w.Service.Now().UTC())
		if err != nil {
			return err
		}
		if attempts >= w.Service.Config.MaximumAttempts {
			w.Telemetry.RecordTerminalFailures(telemetry.AuthenticationEmailDelivery, command.RowsAffected())
		}
		w.Logger.ErrorContext(ctx, "Authentication email attempt failed", "EventId", logging.AuthenticationEmailAttemptFailed, "OutboxId", id, "Category", failure.Category, "AttemptCount", attempts, "CorrelationId", lease, "TraceId", strings.ReplaceAll(lease, "-", ""))
	}
	return nil
}

func retryDelay(attempt int, first, maximum time.Duration) time.Duration {
	if attempt < 1 {
		attempt = 1
	}
	shift := min(attempt-1, 20)
	delay := min(first, maximum)
	for i := 0; i < shift; i++ {
		if delay >= maximum || delay > maximum/2 {
			return maximum
		}
		delay *= 2
	}
	return delay
}

func (w *Worker) cleanupImages(ctx context.Context) error {
	for i := 0; i < w.Service.Config.Jobs().BatchSize; i++ {
		var id, image string
		lease := security.NewID()
		now := w.Service.Now().UTC()
		err := w.Service.Store.Pool.QueryRow(ctx, `UPDATE gift_image_deletion_outbox SET locked_until=$2,lease_id=$3,attempt_count=attempt_count+1 WHERE id=(SELECT id FROM gift_image_deletion_outbox WHERE available_at<=$1 AND (locked_until IS NULL OR locked_until<=$1) ORDER BY available_at FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING id::text,image_id::text`, now, now.Add(w.Service.Config.Jobs().LeaseDuration), lease).Scan(&id, &image)
		if errors.Is(err, pgx.ErrNoRows) {
			return nil
		}
		if err != nil {
			return err
		}
		err = images.Remove(w.Service.Config.ImagePath, image)
		if err == nil || errors.Is(err, os.ErrNotExist) {
			_, err = w.Service.Store.Pool.Exec(ctx, `DELETE FROM gift_image_deletion_outbox WHERE id=$1 AND lease_id=$2`, id, lease)
		} else {
			_, err = w.Service.Store.Pool.Exec(ctx, `UPDATE gift_image_deletion_outbox SET available_at=$2,locked_until=NULL,lease_id=NULL WHERE id=$1 AND lease_id=$3`, id, now.Add(w.Service.Config.Jobs().FirstRetryDelay), lease)
		}
		if err != nil {
			return err
		}
	}
	return nil
}

func (w *Worker) cleanupAuthentication(ctx context.Context) error {
	now := w.Service.Now().UTC()
	batch := w.Service.Config.Jobs().BatchSize
	command, err := w.Service.Store.Pool.Exec(ctx, `UPDATE authentication_email_outbox SET processed_at=$1,protected_payload=NULL,lease_id=NULL,locked_until=NULL,last_error=COALESCE(last_error,'expired') WHERE id IN(SELECT id FROM authentication_email_outbox WHERE processed_at IS NULL AND (locked_until IS NULL OR locked_until<=$1) AND (created_at<=$1::timestamptz-interval '1 day' OR attempt_count>=$3) ORDER BY created_at LIMIT $2)`, now, batch, w.Service.Config.MaximumAttempts)
	if err != nil {
		return err
	}
	w.Telemetry.RecordTerminalFailures(telemetry.AuthenticationEmailDelivery, command.RowsAffected())
	for _, item := range []struct{ Table, Predicate string }{
		{"authentication_access_tokens", `expires_at<=$1`},
		{"authentication_sessions", `expires_at<=$1 OR revoked_at<=$1::timestamptz-interval '30 days'`},
		{"guest_sessions", `expires_at<=$1`},
		{"account_action_tokens", `expires_at<=$1`},
		{"google_authentication_flows", `expires_at<=$1`},
		{"two_factor_challenges", `expires_at<=$1`},
		{"member_email_change_requests", `expires_at<=$1::timestamptz-interval '30 days'`},
		{"member_account_deletion_requests", `expires_at<=$1::timestamptz-interval '1 day'`},
		{"authentication_email_outbox", `processed_at<=$1::timestamptz-interval '30 days'`},
		{"wishlist_moderation_email_outbox", `processed_at<=$1::timestamptz-interval '30 days'`},
		{"users", `NOT email_confirmed AND unconfirmed_account_expires_at<=$1`},
	} {
		statement := `DELETE FROM ` + item.Table + ` WHERE ctid IN(SELECT ctid FROM ` + item.Table + ` WHERE ` + item.Predicate + ` LIMIT $2)`
		if _, err := w.Service.Store.Pool.Exec(ctx, statement, now, batch); err != nil {
			return err
		}
	}
	return nil
}
