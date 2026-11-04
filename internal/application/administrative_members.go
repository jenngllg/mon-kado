package application

import (
	"context"
	"errors"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/jackc/pgx/v5"
)

// AdministrativeMemberResponse identifies an account without disclosing credentials or contact details.
type AdministrativeMemberResponse struct {
	ID             string `json:"id"`
	DisplayName    string `json:"displayName"`
	DisplayNameTag string `json:"displayNameTag"`
}

// AdministrativeMemberSummaryResponse includes current owned-resource counts and confirmed extension usage.
// A nil usage value means attribution was unavailable for this pre-existing account.
type AdministrativeMemberSummaryResponse struct {
	AdministrativeMemberResponse
	CreatedAt            time.Time `json:"createdAt"`
	ListCount            int       `json:"listCount"`
	GroupCount           int       `json:"groupCount"`
	WishCount            int       `json:"wishCount"`
	HasAddedViaExtension *bool     `json:"hasAddedViaExtension"`
}

// AdministrativeMember reads one account for a fresh erasure confirmation or outcome check.
func (s *Service) AdministrativeMember(ctx context.Context, principal Principal, id string) (AdministrativeMemberResponse, error) {
	var item AdministrativeMemberResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, principal); err != nil {
			return err
		}
		return tx.QueryRow(ctx, `SELECT id::text,display_name,display_name_tag FROM users WHERE id=$1`, id).Scan(&item.ID, &item.DisplayName, &item.DisplayNameTag)
	})
	if errors.Is(err, pgx.ErrNoRows) {
		return item, missing(domain.AccountErasureTargetNotFound)
	}
	return item, err
}

// AdministrativeMembers lists accounts newest first, including profiles hidden from public search.
func (s *Service) AdministrativeMembers(ctx context.Context, principal Principal, query string, page, size int) (domain.PaginatedResponse[AdministrativeMemberSummaryResponse], error) {
	items := []AdministrativeMemberSummaryResponse{}
	total := 0
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, principal); err != nil {
			return err
		}
		filter := `($1='' OR strpos(lower(public.unaccent(display_name || ' #' || display_name_tag)),lower(public.unaccent($1)))>0)`
		if err := tx.QueryRow(ctx, `SELECT count(*) FROM users WHERE `+filter, query).Scan(&total); err != nil {
			return err
		}
		// Independent indexed counts avoid multiplying wishes by memberships or visible groups.
		rows, err := tx.Query(ctx, `SELECT id::text,display_name,display_name_tag,created_at,
			(SELECT count(*) FROM wishlists WHERE owner_id=users.id),
			(SELECT count(*) FROM group_members WHERE member_id=users.id),
			(SELECT count(*) FROM wishes w JOIN wishlists l ON l.id=w.wishlist_id WHERE l.owner_id=users.id),
			CASE WHEN EXISTS(SELECT 1 FROM member_extension_usage WHERE member_id=users.id)
				THEN (SELECT has_added FROM member_extension_usage WHERE member_id=users.id) ELSE false END
			FROM users WHERE `+filter+` ORDER BY created_at DESC,id DESC LIMIT $2 OFFSET $3`, query, size, (int64(page)-1)*int64(size))
		if err != nil {
			return err
		}
		defer rows.Close()
		for rows.Next() {
			var item AdministrativeMemberSummaryResponse
			if err := rows.Scan(&item.ID, &item.DisplayName, &item.DisplayNameTag, &item.CreatedAt, &item.ListCount, &item.GroupCount, &item.WishCount, &item.HasAddedViaExtension); err != nil {
				return err
			}
			items = append(items, item)
		}
		return rows.Err()
	})
	return domain.Page(items, page, size, total), err
}
