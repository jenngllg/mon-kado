package application

import (
	"context"
	"fmt"
	"time"

	"github.com/jackc/pgx/v5"
)

// GenerateNotificationReminders creates bounded, idempotent UTC reminders using the injected clock.
func (s *Service) GenerateNotificationReminders(ctx context.Context) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		// A single scheduler at a time also groups occasions across followed lists.
		var locked bool
		if err := tx.QueryRow(ctx, `SELECT pg_try_advisory_xact_lock(771206490)`).Scan(&locked); err != nil {
			return err
		}
		if !locked {
			return nil
		}
		now := s.Now().UTC()
		if err := s.generateOccasionNotifications(ctx, tx, now); err != nil {
			return err
		}
		return s.generateSharingNotifications(ctx, tx, now)
	})
}

type occasionNotification struct {
	member, subscription, wishlist, owner, date string
	days                                        int
}

func (s *Service) generateOccasionNotifications(ctx context.Context, tx pgx.Tx, now time.Time) error {
	rows, err := tx.Query(ctx, `SELECT s.member_id::text,s.id::text,w.id::text,w.owner_id::text,n.event_date::text,n.days_remaining FROM wishlist_subscriptions s JOIN wishlists w ON w.id=s.wishlist_id
CROSS JOIN LATERAL (VALUES (($1::timestamptz AT TIME ZONE 'UTC')::date+7,7),(($1::timestamptz AT TIME ZONE 'UTC')::date+1,1)) n(event_date,days_remaining)
WHERE NOT w.is_archived AND NOT w.is_suspended AND `+notificationOccasionDateMatches+`
AND (NOT EXISTS(SELECT 1 FROM member_notification_preferences p WHERE p.member_id=s.member_id AND 'occasionReminder'=ANY(p.disabled_kinds)) OR EXISTS(SELECT 1 FROM member_push_subscriptions device WHERE device.member_id=s.member_id AND member_push_kind_enabled(device.member_id,'occasionReminder',device.disabled_kinds)))
AND NOT EXISTS(SELECT 1 FROM member_notifications existing WHERE existing.member_id=s.member_id AND existing.deduplication_key='occasion:'||w.owner_id||':'||w.occasion||':'||n.event_date||':'||n.days_remaining AND (existing.in_app OR EXISTS(SELECT 1 FROM member_notification_preferences p WHERE p.member_id=s.member_id AND 'occasionReminder'=ANY(p.disabled_kinds)))) ORDER BY w.id,s.id,n.event_date LIMIT $2`, now, s.Config.Jobs().BatchSize)
	if err != nil {
		return err
	}
	reminders, err := pgx.CollectRows(rows, func(row pgx.CollectableRow) (occasionNotification, error) {
		var r occasionNotification
		err := row.Scan(&r.member, &r.subscription, &r.wishlist, &r.owner, &r.date, &r.days)
		return r, err
	})
	if err != nil {
		return err
	}
	for _, r := range reminders {
		if err = s.notifyOccasion(ctx, tx, r); err != nil {
			return err
		}
	}
	return nil
}

func (s *Service) notifyOccasion(ctx context.Context, tx pgx.Tx, r occasionNotification) error {
	var occasion string
	if err := tx.QueryRow(ctx, `SELECT occasion FROM wishlists WHERE id=$1`, r.wishlist).Scan(&occasion); err != nil {
		return err
	}
	return s.notify(ctx, tx, r.member, notificationEvent{kind: "occasionReminder", actor: r.owner, wishlist: r.wishlist, subscription: r.subscription, days: &r.days, date: &r.date, key: fmt.Sprintf("occasion:%s:%s:%s:%d", r.owner, occasion, r.date, r.days)})
}

func (s *Service) generateSharingNotifications(ctx context.Context, tx pgx.Tx, now time.Time) error {
	rows, err := tx.Query(ctx, `SELECT w.owner_id::text,w.id::text FROM wishlists w WHERE NOT w.is_archived AND NOT w.is_suspended AND NOT w.sharing_reminder_dismissed AND w.created_at<=$1 AND EXISTS(SELECT 1 FROM wishes x WHERE x.wishlist_id=w.id) AND NOT EXISTS(SELECT 1 FROM wishlist_share_links l WHERE l.wishlist_id=w.id) AND NOT EXISTS(SELECT 1 FROM visible_group_wishlists v WHERE v.wishlist_id=w.id) AND (NOT EXISTS(SELECT 1 FROM member_notification_preferences p WHERE p.member_id=w.owner_id AND 'sharingReminder'=ANY(p.disabled_kinds)) OR EXISTS(SELECT 1 FROM member_push_subscriptions device WHERE device.member_id=w.owner_id AND member_push_kind_enabled(device.member_id,'sharingReminder',device.disabled_kinds))) AND NOT EXISTS(SELECT 1 FROM member_notifications n WHERE n.member_id=w.owner_id AND n.deduplication_key='sharing:'||w.id AND (n.in_app OR EXISTS(SELECT 1 FROM member_notification_preferences p WHERE p.member_id=w.owner_id AND 'sharingReminder'=ANY(p.disabled_kinds)))) ORDER BY w.created_at,w.id LIMIT $2`, now.Add(-s.Config.Notifications().SharingReminderDelay), s.Config.Jobs().BatchSize)
	if err != nil {
		return err
	}
	pending, err := pgx.CollectRows(rows, func(row pgx.CollectableRow) ([2]string, error) {
		var r [2]string
		err := row.Scan(&r[0], &r[1])
		return r, err
	})
	if err != nil {
		return err
	}
	for _, r := range pending {
		if err = s.notify(ctx, tx, r[0], notificationEvent{kind: "sharingReminder", wishlist: r[1], key: "sharing:" + r[1]}); err != nil {
			return err
		}
	}
	return nil
}
