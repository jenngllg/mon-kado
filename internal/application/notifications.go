package application

import (
	"context"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

const notificationGroupPrefix = "/groups/"

// Birthdays recur by month/day; February 29 is observed on February 28 in non-leap years.
const notificationOccasionDateMatches = `(CASE WHEN w.occasion='Birthday' THEN
 EXTRACT(MONTH FROM w.event_date)=EXTRACT(MONTH FROM n.event_date)
 AND LEAST(EXTRACT(DAY FROM w.event_date),EXTRACT(DAY FROM (date_trunc('month',n.event_date)+interval '1 month - 1 day')))=EXTRACT(DAY FROM n.event_date)
 ELSE w.event_date=n.event_date END)`

// All projections, including the badge and read commands, use the same live
// authorization predicate. Historical notification identifiers grant no access.
const notificationRelations = ` FROM member_notifications n
LEFT JOIN users actor ON actor.id=n.actor_id
LEFT JOIN wishlist_participants participant ON participant.id=n.participant_id
LEFT JOIN wishlists w ON w.id=n.wishlist_id
LEFT JOIN private_groups g ON g.id=n.group_id
LEFT JOIN wishes wish ON wish.id=n.subject_id AND wish.wishlist_id=w.id
LEFT JOIN group_suggestions suggestion ON suggestion.id=n.subject_id AND suggestion.wishlist_id=w.id AND suggestion.group_id=g.id
LEFT JOIN gift_reservation_histories history ON history.id=n.subject_id AND history.member_id=n.member_id
LEFT JOIN wishlist_subscriptions subscription ON subscription.id=n.subscription_id AND subscription.member_id=n.member_id
WHERE n.member_id=$1
AND (n.wishlist_id IS NULL OR (w.id IS NOT NULL AND NOT w.is_archived AND NOT w.is_suspended))
AND (n.group_id IS NULL OR n.kind='groupInvitation' OR EXISTS(SELECT 1 FROM group_members gm WHERE gm.group_id=n.group_id AND gm.member_id=n.member_id))
AND (n.subscription_id IS NULL OR (subscription.id IS NOT NULL AND (
 (subscription.group_id IS NOT NULL AND EXISTS(SELECT 1 FROM visible_group_wishlists v WHERE v.group_id=subscription.group_id AND v.wishlist_id=w.id)) OR
 (subscription.group_id IS NULL AND EXISTS(SELECT 1 FROM wishlist_share_links l WHERE l.id=subscription.share_link_id AND l.wishlist_id=w.id AND l.secret_hash=subscription.share_secret_hash)))))
AND CASE n.kind
 WHEN 'wishReserved' THEN w.owner_id=n.member_id AND NOT w.surprise_mode AND wish.id IS NOT NULL AND EXISTS(SELECT 1 FROM gift_reservations r WHERE r.wishlist_id=w.id AND r.wish_id=wish.id AND r.wishlist_participant_id=n.participant_id AND n.deduplication_key='reserved:'||r.id)
 WHEN 'wishesAdded' THEN subscription.id IS NOT NULL
 WHEN 'occasionReminder' THEN subscription.id IS NOT NULL AND ` + notificationOccasionDateMatches + ` AND n.event_date>=($2::timestamptz AT TIME ZONE 'UTC')::date
 WHEN 'sharingReminder' THEN w.owner_id=n.member_id AND NOT w.sharing_reminder_dismissed AND EXISTS(SELECT 1 FROM wishes x WHERE x.wishlist_id=w.id) AND NOT EXISTS(SELECT 1 FROM wishlist_share_links l WHERE l.wishlist_id=w.id) AND NOT EXISTS(SELECT 1 FROM visible_group_wishlists v WHERE v.wishlist_id=w.id)
 WHEN 'groupInvitation' THEN EXISTS(SELECT 1 FROM group_invitations i WHERE i.id=n.invitation_id AND i.member_id=n.member_id AND i.expires_at>$2)
 WHEN 'groupJoined' THEN EXISTS(SELECT 1 FROM group_members gm WHERE gm.group_id=g.id AND gm.member_id=n.actor_id)
 WHEN 'groupWishlistShared' THEN w.owner_id<>n.member_id AND EXISTS(SELECT 1 FROM visible_group_wishlists v WHERE v.group_id=g.id AND v.wishlist_id=w.id)
 WHEN 'suggestionAdded' THEN w.owner_id<>n.member_id AND suggestion.id IS NOT NULL AND EXISTS(SELECT 1 FROM visible_group_wishlists v WHERE v.group_id=g.id AND v.wishlist_id=w.id)
 WHEN 'reservationUnavailable' THEN history.id IS NOT NULL AND history.status='Unavailable'
 ELSE false END`

// Notifications returns a consistent, member-private page of currently visible notifications.
func (s *Service) Notifications(ctx context.Context, member string, page, size int) (domain.PaginatedResponse[domain.NotificationResponse], error) {
	items := []domain.NotificationResponse{}
	tx, err := s.Store.Pool.BeginTx(ctx, pgx.TxOptions{IsoLevel: pgx.RepeatableRead, AccessMode: pgx.ReadOnly})
	if err != nil {
		return domain.Page(items, page, size, 0), err
	}
	defer tx.Rollback(ctx)
	now := s.Now().UTC()
	var total int
	if err = tx.QueryRow(ctx, `SELECT count(*)`+notificationRelations, member, now).Scan(&total); err != nil {
		return domain.Page(items, page, size, 0), err
	}
	rows, err := tx.Query(ctx, `SELECT n.id::text,n.kind,COALESCE(actor.display_name,participant.guest_display_name),w.name,g.name,COALESCE(wish.name,history.wish_name),n.quantity,n.days_remaining,n.created_at,n.read_at,n.wishlist_id::text,n.group_id::text,n.subscription_id::text,n.subject_id::text`+notificationRelations+` ORDER BY n.created_at DESC,n.id DESC LIMIT $3 OFFSET $4`, member, now, size, int64(page-1)*int64(size))
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	for rows.Next() {
		var n domain.NotificationResponse
		var list, group, subscription, subject *string
		if err = rows.Scan(&n.Id, &n.Kind, &n.ActorDisplayName, &n.WishlistName, &n.GroupName, &n.WishName, &n.Quantity, &n.DaysRemaining, &n.CreatedAt, &n.ReadAt, &list, &group, &subscription, &subject); err != nil {
			rows.Close()
			return domain.Page(items, page, size, total), err
		}
		switch n.Kind {
		case "wishesAdded", "occasionReminder":
			n.Href = "/followed-lists?notification=" + n.Id
		case "groupInvitation":
			n.Href = "/groups"
		case "groupJoined":
			n.Href = notificationGroupPrefix + *group
		case "groupWishlistShared":
			n.Href = notificationGroupPrefix + *group + "/wishlists/" + *list
		case "suggestionAdded":
			n.Href = notificationGroupPrefix + *group + "/wishlists/" + *list + "/suggestions/" + *subject
		case "reservationUnavailable":
			n.Href = "/reservations"
		default:
			n.Href = "/lists/" + *list
		}
		items = append(items, n)
	}
	err = rows.Err()
	rows.Close()
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	return domain.Page(items, page, size, total), tx.Commit(ctx)
}

// NotificationStatus reads only the live unread indicator.
func (s *Service) NotificationStatus(ctx context.Context, member string) (domain.NotificationStatusResponse, error) {
	var status domain.NotificationStatusResponse
	err := s.Store.Pool.QueryRow(ctx, `SELECT EXISTS(SELECT 1`+notificationRelations+` AND n.read_at IS NULL)`, member, s.Now().UTC()).Scan(&status.HasUnread)
	return status, err
}

// ReadNotifications marks one visible notification, or all visible notifications, as read.
func (s *Service) ReadNotifications(ctx context.Context, member, id string) error {
	command, err := s.Store.Pool.Exec(ctx, `UPDATE member_notifications SET read_at=COALESCE(read_at,$2) WHERE id IN(SELECT n.id`+notificationRelations+` AND ($3='' OR n.id=NULLIF($3,'')::uuid))`, member, s.Now().UTC(), id)
	if err == nil && id != "" && command.RowsAffected() == 0 {
		return missing(domain.NotificationNotFound)
	}
	return err
}

// NotificationDestination resolves an accessible followed list at the instant of navigation.
func (s *Service) NotificationDestination(ctx context.Context, member, id string) (string, error) {
	var subscription string
	err := s.Store.Pool.QueryRow(ctx, `SELECT n.subscription_id::text`+notificationRelations+` AND n.id=$3 AND n.kind IN('wishesAdded','occasionReminder')`, member, s.Now().UTC(), id).Scan(&subscription)
	if errors.Is(err, pgx.ErrNoRows) {
		return "", missing(domain.NotificationNotFound)
	}
	if err != nil {
		return "", err
	}
	value, err := s.Subscription(ctx, member, subscription)
	return value.ShareUrl, err
}

// DismissSharingReminder permanently dismisses the owner's optional sharing reminder.
func (s *Service) DismissSharingReminder(ctx context.Context, member, id string) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var wishlist string
		err := tx.QueryRow(ctx, `SELECT n.wishlist_id::text`+notificationRelations+` AND n.id=$3 AND n.kind='sharingReminder'`, member, s.Now().UTC(), id).Scan(&wishlist)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.NotificationNotFound)
		}
		if err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `UPDATE wishlists SET sharing_reminder_dismissed=true WHERE id=$1 AND owner_id=$2`, wishlist, member)
		return err
	})
}

type notificationEvent struct {
	kind, actor, participant, wishlist, group, subscription, invitation, subject, key string
	days                                                                              *int
	date                                                                              *string
}

func (s *Service) notify(ctx context.Context, tx pgx.Tx, member string, e notificationEvent) error {
	if member == e.actor {
		return nil
	}
	now := s.Now().UTC()
	if e.kind == "wishesAdded" {
		command, err := tx.Exec(ctx, `UPDATE member_notifications SET quantity=quantity+1 WHERE id=(SELECT id FROM member_notifications WHERE member_id=$1 AND subscription_id=$2 AND kind='wishesAdded' AND read_at IS NULL AND created_at>=$3 AND NOT EXISTS(SELECT 1 FROM member_notification_preferences p WHERE p.member_id=$1 AND 'wishesAdded'=ANY(p.disabled_kinds)) ORDER BY created_at DESC,id DESC LIMIT 1 FOR UPDATE)`, member, e.subscription, now.Add(-s.Config.Notifications().GroupingWindow))
		if err != nil || command.RowsAffected() > 0 {
			return err
		}
	}
	_, err := tx.Exec(ctx, `INSERT INTO member_notifications(id,member_id,kind,actor_id,participant_id,wishlist_id,group_id,subscription_id,invitation_id,subject_id,deduplication_key,created_at,days_remaining,event_date) SELECT $1,$2,$3,NULLIF($4,'')::uuid,NULLIF($5,'')::uuid,NULLIF($6,'')::uuid,NULLIF($7,'')::uuid,NULLIF($8,'')::uuid,NULLIF($9,'')::uuid,NULLIF($10,'')::uuid,NULLIF($11,''),$12,$13,$14::date WHERE NOT EXISTS(SELECT 1 FROM member_notification_preferences p WHERE p.member_id=$2 AND $3=ANY(p.disabled_kinds)) ON CONFLICT(member_id,deduplication_key) DO NOTHING`, security.NewID(), member, e.kind, e.actor, e.participant, e.wishlist, e.group, e.subscription, e.invitation, e.subject, e.key, now, e.days, e.date)
	return err
}

func (s *Service) notifyRecipients(ctx context.Context, tx pgx.Tx, e notificationEvent, query string, args ...any) error {
	rows, err := tx.Query(ctx, query, args...)
	if err != nil {
		return err
	}
	members, err := pgx.CollectRows(rows, pgx.RowTo[string])
	if err != nil {
		return err
	}
	for _, member := range members {
		if err = s.notify(ctx, tx, member, e); err != nil {
			return err
		}
	}
	return nil
}

func (s *Service) notifyWishAdded(ctx context.Context, tx pgx.Tx, owner, wishlist string) error {
	rows, err := tx.Query(ctx, `SELECT id::text,member_id::text FROM wishlist_subscriptions WHERE wishlist_id=$1 ORDER BY id`, wishlist)
	if err != nil {
		return err
	}
	type follow struct{ id, member string }
	follows, err := pgx.CollectRows(rows, func(row pgx.CollectableRow) (follow, error) {
		var f follow
		err := row.Scan(&f.id, &f.member)
		return f, err
	})
	if err != nil {
		return err
	}
	for _, f := range follows {
		if err = s.notify(ctx, tx, f.member, notificationEvent{kind: "wishesAdded", actor: owner, wishlist: wishlist, subscription: f.id}); err != nil {
			return err
		}
	}
	return nil
}

func (s *Service) notifyGroupJoined(ctx context.Context, tx pgx.Tx, member, group string) error {
	return s.notifyRecipients(ctx, tx, notificationEvent{kind: "groupJoined", actor: member, group: group}, `SELECT member_id::text FROM group_members WHERE group_id=$1 ORDER BY member_id`, group)
}

func (s *Service) notifyGroupShared(ctx context.Context, tx pgx.Tx, owner, wishlist, group string) error {
	return s.notifyRecipients(ctx, tx, notificationEvent{kind: "groupWishlistShared", actor: owner, wishlist: wishlist, group: group}, `SELECT member_id::text FROM group_members WHERE group_id=$1 ORDER BY member_id`, group)
}

func (s *Service) notifyUnavailableWish(ctx context.Context, tx pgx.Tx, wishlist, wish string) error {
	rows, err := tx.Query(ctx, `SELECT h.member_id::text,h.id::text FROM gift_reservation_histories h JOIN wishlists w ON w.id=h.wishlist_id WHERE h.wishlist_id=$1 AND h.wish_id=$2 AND h.status='Active' AND h.member_id<>w.owner_id ORDER BY h.id`, wishlist, wish)
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
		if err = s.notify(ctx, tx, r[0], notificationEvent{kind: "reservationUnavailable", wishlist: wishlist, subject: r[1], key: "unavailable:" + r[1]}); err != nil {
			return err
		}
	}
	return nil
}
