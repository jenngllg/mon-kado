package worker

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/logging"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
	"strings"
)

func (w *Worker) deliverNotifications(ctx context.Context, erasure bool) error {
	for i := 0; i < w.Service.Config.Jobs().BatchSize; i++ {
		var id, lease string
		now := w.Service.Now().UTC()
		lease = security.NewID()
		table := "wishlist_moderation_email_outbox"
		eligibility := ""
		if erasure {
			table = "account_erasure_email_outbox"
			eligibility = " AND expires_at>$1"
		}
		err := w.Service.Store.Pool.QueryRow(ctx, `UPDATE `+table+` SET lease_id=$2,locked_until=$3,attempt_count=attempt_count+1 WHERE id=(SELECT id FROM `+table+` WHERE available_at<=$1 AND (locked_until IS NULL OR locked_until<=$1) AND attempt_count<$4`+eligibility+func() string {
			if !erasure {
				return " AND processed_at IS NULL"
			}
			return ""
		}()+` ORDER BY created_at,id FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING id::text`, now, lease, now.Add(w.Service.Config.Jobs().LeaseDuration), w.Service.Config.MaximumAttempts).Scan(&id)
		if errors.Is(err, pgx.ErrNoRows) {
			break
		}
		if err != nil {
			return err
		}
		message := application.EmailMessage{Subject: "Votre compte a été supprimé – MonKado", Text: "Bonjour,\n\nVotre compte MonKado et les données personnelles associées ont été supprimés. Si vous n'êtes pas à l'origine de cette opération, contactez l'assistance MonKado."}
		if erasure {
			var protected string
			err = w.Service.Store.Pool.QueryRow(ctx, `SELECT protected_recipient FROM account_erasure_email_outbox WHERE id=$1 AND lease_id=$2`, id, lease).Scan(&protected)
			if err == nil {
				message.To, err = w.Service.Crypto.Unprotect(protected, "erasure-recipient|"+id)
			}
		} else {
			var action string
			var reason *string
			err = w.Service.Store.Pool.QueryRow(ctx, `SELECT u.email,e.action,e.reason FROM wishlist_moderation_events e JOIN wishlists l ON l.id=e.wishlist_id JOIN users u ON u.id=l.owner_id WHERE e.id=$1`, id).Scan(&message.To, &action, &reason)
			if err == nil {
				decision := "Votre liste a été suspendue"
				if action == "Reactivated" {
					decision = "Votre liste a été réactivée"
				}
				if action == "ReasonUpdated" {
					decision = "Le motif de suspension de votre liste a été modifié"
				}
				message.Subject = decision + " – MonKado"
				message.Text = "Bonjour,\n\n" + decision + ". Consultez votre compte pour connaître son état : " + w.Service.Config.FrontendOrigin
				if reason != nil {
					message.Text += "\n\nMotif : " + *reason
				}
			}
		}
		if errors.Is(err, pgx.ErrNoRows) {
			continue
		}
		if err == nil {
			_, err = w.Sender.Send(ctx, id, message)
		}
		if ctx.Err() != nil {
			return ctx.Err()
		}
		if err == nil {
			if erasure {
				err = w.Service.Store.Transaction(ctx, func(tx pgx.Tx) error {
					command, err := tx.Exec(ctx, `DELETE FROM account_erasure_email_outbox WHERE id=$1 AND lease_id=$2`, id, lease)
					if err != nil {
						return err
					}
					if command.RowsAffected() != 1 {
						return nil
					}
					_, err = tx.Exec(ctx, `UPDATE administrative_account_erasure_events SET notification_status='Accepted' WHERE id=$1`, id)
					return err
				})
			} else {
				_, err = w.Service.Store.Pool.Exec(ctx, `UPDATE wishlist_moderation_email_outbox SET processed_at=$3,lease_id=NULL,locked_until=NULL,last_error=NULL WHERE id=$1 AND lease_id=$2`, id, lease, w.Service.Now().UTC())
			}
			if err != nil {
				return err
			}
			continue
		}
		failure := &application.DeliveryError{Category: "processing", Retryable: true}
		errors.As(err, &failure)
		var count int
		if err = w.Service.Store.Pool.QueryRow(ctx, `SELECT attempt_count FROM `+table+` WHERE id=$1 AND lease_id=$2`, id, lease).Scan(&count); err != nil {
			return err
		}
		if !failure.Retryable {
			count = w.Service.Config.MaximumAttempts
		}
		if erasure && count >= w.Service.Config.MaximumAttempts {
			err = w.Service.Store.Transaction(ctx, func(tx pgx.Tx) error {
				command, err := tx.Exec(ctx, `DELETE FROM account_erasure_email_outbox WHERE id=$1 AND lease_id=$2`, id, lease)
				if err != nil {
					return err
				}
				if command.RowsAffected() != 1 {
					return nil
				}
				_, err = tx.Exec(ctx, `UPDATE administrative_account_erasure_events SET notification_status='Failed' WHERE id=$1`, id)
				return err
			})
		} else {
			delay := max(retryDelay(count, w.Service.Config.Jobs().FirstRetryDelay, w.Service.Config.Jobs().MaximumRetryDelay), min(failure.RetryAfter, w.Service.Config.Jobs().MaximumRetryDelay))
			update := `UPDATE ` + table + ` SET available_at=$3,attempt_count=$4,locked_until=NULL,lease_id=NULL`
			if !erasure {
				update += `,last_error=$5,processed_at=CASE WHEN $4::integer>=$6::integer THEN $7::timestamptz ELSE NULL END WHERE id=$1 AND lease_id=$2`
				_, err = w.Service.Store.Pool.Exec(ctx, update, id, lease, w.Service.Now().UTC().Add(delay), count, failure.Category, w.Service.Config.MaximumAttempts, w.Service.Now().UTC())
			} else {
				update += ` WHERE id=$1 AND lease_id=$2`
				_, err = w.Service.Store.Pool.Exec(ctx, update, id, lease, w.Service.Now().UTC().Add(delay), count)
			}
		}
		if err != nil {
			return err
		}
		w.Logger.ErrorContext(ctx, "Notification attempt failed", "EventId", logging.NotificationAttemptFailed, "OutboxId", id, "FailureCategory", failure.Category, "CorrelationId", lease, "TraceId", strings.ReplaceAll(lease, "-", ""))
	}
	return nil
}

func (w *Worker) cleanupExpiredErasure(ctx context.Context) error {
	return w.Service.Store.Transaction(ctx, func(tx pgx.Tx) error {
		_, err := tx.Exec(ctx, `WITH expired AS(DELETE FROM account_erasure_email_outbox WHERE id IN(SELECT id FROM account_erasure_email_outbox WHERE expires_at<=$1 ORDER BY expires_at LIMIT $2) RETURNING id) UPDATE administrative_account_erasure_events SET notification_status='Failed' WHERE id IN(SELECT id FROM expired)`, w.Service.Now().UTC(), w.Service.Config.Jobs().BatchSize)
		return err
	})
}
