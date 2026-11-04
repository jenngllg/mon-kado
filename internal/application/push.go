package application

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
	"time"
)

// PushSubscriptions lists caller-owned devices with live sessions, without capability URLs.
func (s *Service) PushSubscriptions(ctx context.Context, p Principal) ([]domain.PushSubscriptionResponse, error) {
	items := []domain.PushSubscriptionResponse{}
	rows, err := s.Store.Pool.Query(ctx, `SELECT p.id::text,p.name,p.endpoint_hash,p.disabled_kinds,p.created_at,p.xmin::text::bigint FROM member_push_subscriptions p JOIN authentication_sessions sess ON sess.id=p.session_id WHERE p.member_id=$1 AND sess.user_id=p.member_id AND sess.revoked_at IS NULL AND sess.expires_at>$2 ORDER BY p.created_at,p.id`, p.MemberID, s.Now().UTC())
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	for rows.Next() {
		var item domain.PushSubscriptionResponse
		var version uint32
		if err = rows.Scan(&item.Id, &item.Name, &item.EndpointHash, &item.DisabledKinds, &item.CreatedAt, &version); err != nil {
			return nil, err
		}
		item.Version = EntityTag(version)
		items = append(items, item)
	}
	return items, rows.Err()
}

// CreatePushSubscription binds a device to this account without replaying historical notifications.
func (s *Service) CreatePushSubscription(ctx context.Context, p Principal, input domain.CreatePushSubscriptionRequest) (domain.PushSubscriptionResponse, error) {
	result := domain.PushSubscriptionResponse{Id: security.NewID(), Name: input.Name, DisabledKinds: []string{}, CreatedAt: s.Now().UTC()}
	if !s.Config.PushOptions.Enabled {
		return result, domain.Failure(503, domain.PushUnavailable, "Push notifications are not configured.")
	}
	hash := sha256.Sum256([]byte(input.Endpoint))
	result.EndpointHash = hex.EncodeToString(hash[:])
	// Missing initial choices preserve the original API default; explicit choices apply atomically.
	initialDisabled := input.DisabledKinds
	if initialDisabled == nil {
		initialDisabled = []string{}
	}
	protected, err := s.Crypto.Protect(input.Endpoint, "push-endpoint")
	if err != nil {
		return result, err
	}
	var version uint32
	err = s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		// Account locking bounds registrations across concurrent sessions.
		var member string
		if err := tx.QueryRow(ctx, `SELECT id::text FROM users WHERE id=$1 FOR UPDATE`, p.MemberID).Scan(&member); err != nil {
			return err
		}
		var count int
		if err := tx.QueryRow(ctx, `SELECT count(*) FROM member_push_subscriptions WHERE member_id=$1 AND endpoint_hash<>$2`, p.MemberID, result.EndpointHash).Scan(&count); err != nil {
			return err
		}
		if count >= 10 {
			return domain.Failure(409, domain.PushDeviceLimit, "The device subscription limit has been reached.")
		}
		// Transfer to a newly signed-in account must not transfer the previous account's queue.
		if _, err := tx.Exec(ctx, `DELETE FROM member_push_deliveries WHERE subscription_id IN(SELECT id FROM member_push_subscriptions WHERE endpoint_hash=$1 AND (member_id<>$2 OR session_id<>$3))`, result.EndpointHash, p.MemberID, p.SessionID); err != nil {
			return err
		}
		return tx.QueryRow(ctx, `INSERT INTO member_push_subscriptions(id,member_id,session_id,name,endpoint_hash,protected_endpoint,created_at,disabled_kinds) SELECT $1,$2,$3,$4,$5,$6,$7,$8 WHERE EXISTS(SELECT 1 FROM authentication_sessions WHERE id=$3 AND user_id=$2 AND revoked_at IS NULL AND expires_at>$7) ON CONFLICT(endpoint_hash) DO UPDATE SET member_id=excluded.member_id,session_id=excluded.session_id,name=CASE WHEN member_push_subscriptions.member_id=excluded.member_id THEN member_push_subscriptions.name ELSE excluded.name END,disabled_kinds=CASE WHEN member_push_subscriptions.member_id=excluded.member_id AND $9 THEN member_push_subscriptions.disabled_kinds ELSE excluded.disabled_kinds END,protected_endpoint=excluded.protected_endpoint RETURNING id::text,name,disabled_kinds,created_at,xmin::text::bigint`, result.Id, p.MemberID, p.SessionID, input.Name, result.EndpointHash, protected, result.CreatedAt, initialDisabled, input.DisabledKinds == nil).Scan(&result.Id, &result.Name, &result.DisabledKinds, &result.CreatedAt, &version)
	})
	result.Version = EntityTag(version)
	return result, err
}

// UpdatePushSubscription saves a caller-owned device with optimistic concurrency.
func (s *Service) UpdatePushSubscription(ctx context.Context, p Principal, id string, input domain.UpdatePushSubscriptionRequest, expected uint32) (domain.PushSubscriptionResponse, uint32, error) {
	result := domain.PushSubscriptionResponse{Id: id, Name: input.Name, DisabledKinds: input.DisabledKinds}
	var version uint32
	err := s.Store.Pool.QueryRow(ctx, `UPDATE member_push_subscriptions SET name=$3,disabled_kinds=$4 WHERE id=$1 AND member_id=$2 AND xmin::text::bigint=$5 RETURNING endpoint_hash,created_at,xmin::text::bigint`, id, p.MemberID, input.Name, input.DisabledKinds, expected).Scan(&result.EndpointHash, &result.CreatedAt, &version)
	if errors.Is(err, pgx.ErrNoRows) {
		var exists bool
		if e := s.Store.Pool.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM member_push_subscriptions WHERE id=$1 AND member_id=$2)`, id, p.MemberID).Scan(&exists); e != nil {
			return result, 0, e
		}
		if !exists {
			return result, 0, missing(domain.PushSubscriptionNotFound)
		}
		return result, 0, stale(domain.PushSubscriptionVersionConflict)
	}
	result.Version = EntityTag(version)
	return result, version, err
}

// DeletePushSubscription removes a caller-owned device and its pending deliveries.
func (s *Service) DeletePushSubscription(ctx context.Context, p Principal, id string) error {
	command, err := s.Store.Pool.Exec(ctx, `DELETE FROM member_push_subscriptions WHERE id=$1 AND member_id=$2`, id, p.MemberID)
	if err == nil && command.RowsAffected() == 0 {
		return missing(domain.PushSubscriptionNotFound)
	}
	return err
}

// PushSender delivers an empty visible signal; errors never expose capability URLs.
type PushSender interface {
	Send(context.Context, string) (int, error)
}

// DeliverPush drains a bounded durable outbox with current authorization before each attempt.
func (s *Service) DeliverPush(ctx context.Context, sender PushSender) error {
	now := s.Now().UTC()
	if _, err := s.Store.Pool.Exec(ctx, `DELETE FROM member_push_subscriptions p WHERE NOT EXISTS(SELECT 1 FROM authentication_sessions sess WHERE sess.id=p.session_id AND sess.user_id=p.member_id AND sess.revoked_at IS NULL AND sess.expires_at>$1)`, now); err != nil {
		return err
	}
	if _, err := s.Store.Pool.Exec(ctx, `DELETE FROM member_push_deliveries WHERE due_at<$1`, now.Add(-7*24*time.Hour)); err != nil {
		return err
	}
	if sender == nil || !s.Config.PushOptions.Enabled {
		return nil
	}
	// A lost acknowledgement has an uncertain outcome. Never silently replay it.
	if _, err := s.Store.Pool.Exec(ctx, `UPDATE member_push_deliveries SET status='failed',lease_id=NULL,lease_expires_at=NULL WHERE status='sending' AND lease_expires_at<=$1`, now); err != nil {
		return err
	}
	for range s.Config.Jobs().BatchSize {
		lease := security.NewID()
		var notification, subscription, member, protected string
		var attempts int
		err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
			err := tx.QueryRow(ctx, `SELECT d.notification_id::text,d.subscription_id::text,p.member_id::text,p.protected_endpoint,d.attempts FROM member_push_deliveries d JOIN member_push_subscriptions p ON p.id=d.subscription_id WHERE d.status='pending' AND d.due_at<=$1 ORDER BY d.due_at,d.notification_id,d.subscription_id LIMIT 1 FOR UPDATE OF d SKIP LOCKED`, now).Scan(&notification, &subscription, &member, &protected, &attempts)
			if err != nil {
				return err
			}
			_, err = tx.Exec(ctx, `UPDATE member_push_deliveries SET status='sending',attempts=attempts+1,lease_id=$3,lease_expires_at=$4 WHERE notification_id=$1 AND subscription_id=$2`, notification, subscription, lease, now.Add(s.Config.Jobs().LeaseDuration))
			return err
		})
		if errors.Is(err, pgx.ErrNoRows) {
			return nil
		}
		if err != nil {
			return err
		}
		var allowed bool
		err = s.Store.Pool.QueryRow(ctx, `SELECT EXISTS(SELECT 1`+notificationAuthorizationRelations+` AND n.id=$3 AND n.read_at IS NULL AND EXISTS(SELECT 1 FROM member_push_subscriptions p JOIN authentication_sessions sess ON sess.id=p.session_id WHERE p.id=$4 AND p.member_id=n.member_id AND member_push_kind_enabled(p.member_id,n.kind,p.disabled_kinds) AND sess.user_id=p.member_id AND sess.revoked_at IS NULL AND sess.expires_at>$2))`, member, s.Now().UTC(), notification, subscription).Scan(&allowed)
		if err != nil {
			return err
		}
		state := "cancelled"
		due := now
		if allowed {
			endpoint, decodeErr := s.Crypto.Unprotect(protected, "push-endpoint")
			if decodeErr != nil {
				return decodeErr
			}
			status, sendErr := sender.Send(ctx, endpoint)
			state = "failed"
			if sendErr == nil {
				switch {
				case status >= 200 && status < 300:
					state = "sent"
				case status == 404 || status == 410:
					if _, err = s.Store.Pool.Exec(ctx, `DELETE FROM member_push_subscriptions WHERE id=$1 AND member_id=$2`, subscription, member); err != nil {
						return err
					}
					continue
				case (status == 429 || status >= 500) && attempts+1 < s.Config.MaximumAttempts:
					state = "pending"
					delay := s.Config.Jobs().FirstRetryDelay
					for range attempts {
						if delay >= s.Config.Jobs().MaximumRetryDelay/2 {
							delay = s.Config.Jobs().MaximumRetryDelay
							break
						}
						delay *= 2
					}
					due = now.Add(delay)
				}
			}
		}
		if _, err = s.Store.Pool.Exec(ctx, `UPDATE member_push_deliveries SET status=$4,due_at=$5,lease_id=NULL,lease_expires_at=NULL WHERE notification_id=$1 AND subscription_id=$2 AND lease_id=$3 AND lease_expires_at>$6`, notification, subscription, lease, state, due, s.Now().UTC()); err != nil {
			return err
		}
	}
	return nil
}
