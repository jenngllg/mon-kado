package application

import (
	"context"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/jackc/pgx/v5"
)

// SubscriptionOwners returns only owner identities already authorized through live follows.
// Its separate additive resource preserves the existing subscription response contract.
func (s *Service) SubscriptionOwners(ctx context.Context, member string, page, size int) (domain.PaginatedResponse[domain.WishlistSubscriptionOwnerResponse], error) {
	items := []domain.WishlistSubscriptionOwnerResponse{}
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
		rows, queryErr := tx.Query(ctx, `SELECT w.id::text,w.owner_id::text`+subscriptionScope+` ORDER BY s.created_at DESC,s.id LIMIT $2 OFFSET $3`, member, size, (int64(page)-1)*int64(size))
		if queryErr != nil {
			return domain.Page(items, page, size, total), queryErr
		}
		items, err = pgx.CollectRows(rows, func(row pgx.CollectableRow) (domain.WishlistSubscriptionOwnerResponse, error) {
			var item domain.WishlistSubscriptionOwnerResponse
			err := row.Scan(&item.WishlistId, &item.OwnerId)
			return item, err
		})
		if err != nil {
			return domain.Page(items, page, size, total), err
		}
	}
	return domain.Page(items, page, size, total), tx.Commit(ctx)
}
