package application

import (
	"context"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

const subscriptionColumns = `s.id::text,w.id::text,w.name,u.display_name,w.occasion,w.event_date::text,s.created_at,l.id::text,l.protected_secret,s.group_id::text`
const subscriptionScope = ` FROM wishlist_subscriptions s LEFT JOIN wishlist_share_links l ON l.id=s.share_link_id AND l.wishlist_id=s.wishlist_id AND l.secret_hash=s.share_secret_hash JOIN wishlists w ON w.id=s.wishlist_id JOIN users u ON u.id=w.owner_id WHERE s.member_id=$1 AND NOT w.is_suspended AND NOT w.is_archived AND (l.id IS NOT NULL OR (s.group_id IS NOT NULL AND EXISTS(SELECT 1 FROM visible_group_wishlists gw JOIN group_members gm ON gm.group_id=gw.group_id AND gm.member_id=s.member_id WHERE gw.group_id=s.group_id AND gw.wishlist_id=s.wishlist_id)))`

func (s *Service) scanSubscription(row pgx.Row) (domain.WishlistSubscriptionResponse, error) {
	var result domain.WishlistSubscriptionResponse
	var link, protected, group *string
	err := row.Scan(&result.Id, &result.WishlistId, &result.Name, &result.OwnerDisplayName, &result.Occasion, &result.EventDate, &result.CreatedAt, &link, &protected, &group)
	if errors.Is(err, pgx.ErrNoRows) {
		return result, missing(domain.WishlistSubscriptionNotFound)
	}
	if err != nil {
		return result, err
	}
	result.Occasion = lowerFirst(result.Occasion)
	if group != nil {
		result.ShareUrl = s.Config.FrontendOrigin + "/groups/" + *group + "/wishlists/" + result.WishlistId
		return result, nil
	}
	secret, err := s.Crypto.Unprotect(*protected, "share|"+*link)
	if err != nil {
		return result, err
	}
	result.ShareUrl = s.Config.FrontendOrigin + "/shared-wishlists/" + *link + "#" + secret
	return result, nil
}

// CreateSubscription pins the verified capability without joining or reserving any wishes.
func (s *Service) CreateSubscription(ctx context.Context, member, link, secret string) (domain.WishlistSubscriptionResponse, error) {
	var result domain.WishlistSubscriptionResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyShare(ctx, tx, link, secret)
		if err != nil {
			return err
		}
		if share.Owner == member {
			return domain.Failure(409, domain.WishlistSubscriptionSelf, "The owner cannot subscribe to their own wishlist.")
		}
		id := security.NewID()
		_, err = tx.Exec(ctx, `INSERT INTO wishlist_subscriptions(id,member_id,wishlist_id,share_link_id,share_secret_hash,created_at) VALUES($1,$2,$3,$4,$5,$6)`, id, member, share.Wishlist, link, security.HashToken(secret), s.Now().UTC())
		if err != nil {
			return err
		}
		result, err = s.scanSubscription(tx.QueryRow(ctx, `SELECT `+subscriptionColumns+subscriptionScope+` AND s.id=$2`, member, id))
		return err
	})
	if duplicate(err, "ux_wishlist_subscriptions_member_wishlist") {
		err = domain.Failure(409, domain.WishlistSubscriptionAlreadyExists, "A subscription to this wishlist already exists.")
	}
	return result, err
}

// Subscription retrieves an owned summary only while its original capability remains valid.
func (s *Service) Subscription(ctx context.Context, member, id string) (domain.WishlistSubscriptionResponse, error) {
	return s.scanSubscription(s.Store.Pool.QueryRow(ctx, `SELECT `+subscriptionColumns+subscriptionScope+` AND s.id=$2`, member, id))
}

// CurrentSubscription verifies the presented capability before resolving the member's subscription.
func (s *Service) CurrentSubscription(ctx context.Context, member, link, secret string) (domain.WishlistSubscriptionResponse, error) {
	var result domain.WishlistSubscriptionResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyShare(ctx, tx, link, secret)
		if err != nil {
			return err
		}
		result, err = s.scanSubscription(tx.QueryRow(ctx, `SELECT `+subscriptionColumns+subscriptionScope+` AND s.wishlist_id=$2`, member, share.Wishlist))
		return err
	})
	return result, err
}

// CreateGroupSubscription follows an accessible group list without creating public access or participation.
func (s *Service) CreateGroupSubscription(ctx context.Context, member, group, wishlist string) (domain.WishlistSubscriptionResponse, error) {
	var result domain.WishlistSubscriptionResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyGroupWishlist(ctx, tx, member, group, wishlist)
		if err != nil {
			return err
		}
		if share.Owner == member {
			return domain.Failure(409, domain.WishlistSubscriptionSelf, "The owner cannot subscribe to their own wishlist.")
		}
		id := security.NewID()
		_, err = tx.Exec(ctx, `INSERT INTO wishlist_subscriptions(id,member_id,wishlist_id,group_id,created_at) VALUES($1,$2,$3,$4,$5)`, id, member, wishlist, group, s.Now().UTC())
		if err != nil {
			return err
		}
		result, err = s.scanSubscription(tx.QueryRow(ctx, `SELECT `+subscriptionColumns+subscriptionScope+` AND s.id=$2`, member, id))
		return err
	})
	if duplicate(err, "ux_wishlist_subscriptions_member_wishlist") {
		err = domain.Failure(409, domain.WishlistSubscriptionAlreadyExists, "A subscription to this wishlist already exists.")
	}
	return result, err
}

// CurrentGroupSubscription rechecks private access before reading the caller's follow state.
func (s *Service) CurrentGroupSubscription(ctx context.Context, member, group, wishlist string) (domain.WishlistSubscriptionResponse, error) {
	var result domain.WishlistSubscriptionResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, err := s.verifyGroupWishlist(ctx, tx, member, group, wishlist); err != nil {
			return err
		}
		var err error
		result, err = s.scanSubscription(tx.QueryRow(ctx, `SELECT `+subscriptionColumns+subscriptionScope+` AND s.wishlist_id=$2`, member, wishlist))
		return err
	})
	return result, err
}

// Subscriptions reads matching items and counts from one consistent snapshot.
func (s *Service) Subscriptions(ctx context.Context, member string, page, size int) (domain.PaginatedResponse[domain.WishlistSubscriptionResponse], error) {
	items := []domain.WishlistSubscriptionResponse{}
	var total int
	tx, err := s.Store.Pool.BeginTx(ctx, pgx.TxOptions{IsoLevel: pgx.RepeatableRead, AccessMode: pgx.ReadOnly})
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer tx.Rollback(ctx)
	if err = tx.QueryRow(ctx, `SELECT count(*)`+subscriptionScope, member).Scan(&total); err != nil {
		return domain.Page(items, page, size, total), err
	}
	if total != 0 && int64(page) <= (int64(total)+int64(size)-1)/int64(size) {
		rows, err := tx.Query(ctx, `SELECT `+subscriptionColumns+subscriptionScope+` ORDER BY s.created_at DESC,s.id LIMIT $2 OFFSET $3`, member, size, (int64(page)-1)*int64(size))
		if err != nil {
			return domain.Page(items, page, size, total), err
		}
		for rows.Next() {
			item, err := s.scanSubscription(rows)
			if err != nil {
				rows.Close()
				return domain.Page(items, page, size, total), err
			}
			items = append(items, item)
		}
		rows.Close()
		if err = rows.Err(); err != nil {
			return domain.Page(items, page, size, total), err
		}
	}
	return domain.Page(items, page, size, total), tx.Commit(ctx)
}

// DeleteSubscription removes an owned subscription even after its capability was revoked.
func (s *Service) DeleteSubscription(ctx context.Context, member, id string) error {
	command, err := s.Store.Pool.Exec(ctx, `DELETE FROM wishlist_subscriptions WHERE id=$1 AND member_id=$2`, id, member)
	if err != nil {
		return err
	}
	if command.RowsAffected() == 0 {
		return missing(domain.WishlistSubscriptionNotFound)
	}
	return nil
}
