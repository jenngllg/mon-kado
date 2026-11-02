package application

import (
	"context"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/jackc/pgx/v5"
)

// AdministrativeMemberResponse identifies an account without disclosing credentials or contact details.
type AdministrativeMemberResponse struct {
	ID             string `json:"id"`
	DisplayName    string `json:"displayName"`
	DisplayNameTag string `json:"displayNameTag"`
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
func (s *Service) AdministrativeMembers(ctx context.Context, principal Principal, query string, page, size int) (domain.PaginatedResponse[AdministrativeMemberResponse], error) {
	items := []AdministrativeMemberResponse{}
	total := 0
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, principal); err != nil {
			return err
		}
		filter := `($1='' OR strpos(lower(public.unaccent(display_name || ' #' || display_name_tag)),lower(public.unaccent($1)))>0)`
		if err := tx.QueryRow(ctx, `SELECT count(*) FROM users WHERE `+filter, query).Scan(&total); err != nil {
			return err
		}
		rows, err := tx.Query(ctx, `SELECT id::text,display_name,display_name_tag FROM users WHERE `+filter+` ORDER BY created_at DESC,id DESC LIMIT $2 OFFSET $3`, query, size, (int64(page)-1)*int64(size))
		if err != nil {
			return err
		}
		defer rows.Close()
		for rows.Next() {
			var item AdministrativeMemberResponse
			if err := rows.Scan(&item.ID, &item.DisplayName, &item.DisplayNameTag); err != nil {
				return err
			}
			items = append(items, item)
		}
		return rows.Err()
	})
	return domain.Page(items, page, size, total), err
}
