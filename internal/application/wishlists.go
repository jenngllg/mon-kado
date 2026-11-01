package application

import (
	"context"
	"errors"
	"fmt"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

const wishlistColumns = `id::text,name,occasion,event_date::text,message,created_at,updated_at,is_suspended,suspension_reason,suspended_at,surprise_mode,is_archived,xmin::text::bigint`

func scanWishlist(row pgx.Row) (domain.WishlistResponse, uint32, error) {
	var r domain.WishlistResponse
	var v uint32
	err := row.Scan(&r.Id, &r.Name, &r.Occasion, &r.EventDate, &r.Message, &r.CreatedAt, &r.UpdatedAt, &r.IsSuspended, &r.SuspensionReason, &r.SuspendedAt, &r.SurpriseMode, &r.IsArchived, &v)
	r.Occasion = lowerFirst(r.Occasion)
	return r, v, err
}

// Wishlists retrieves owned private lists in reverse creation order.
func (s *Service) Wishlists(ctx context.Context, owner string, archived bool) ([]domain.WishlistResponse, error) {
	rows, err := s.Store.Pool.Query(ctx, `SELECT `+wishlistColumns+` FROM wishlists WHERE owner_id=$1 AND is_archived=$2 ORDER BY created_at DESC,id DESC`, owner, archived)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := []domain.WishlistResponse{}
	for rows.Next() {
		r, _, err := scanWishlist(rows)
		if err != nil {
			return nil, err
		}
		result = append(result, r)
	}
	return result, rows.Err()
}

// Wishlist retrieves one owned private list and its current concurrency version.
func (s *Service) Wishlist(ctx context.Context, owner, id string) (domain.WishlistResponse, uint32, error) {
	r, v, err := scanWishlist(s.Store.Pool.QueryRow(ctx, `SELECT `+wishlistColumns+` FROM wishlists WHERE id=$1 AND owner_id=$2`, id, owner))
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.WishlistNotFound)
	}
	return r, v, err
}

// CreateWishlist creates the list, relying on PostgreSQL for normalized-name uniqueness.
func (s *Service) CreateWishlist(ctx context.Context, owner string, request domain.CreateWishlistRequest) (domain.WishlistResponse, uint32, error) {
	name := strings.TrimSpace(text(request.Name))
	var r domain.WishlistResponse
	var v uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var err error
		r, v, err = scanWishlist(tx.QueryRow(ctx, `INSERT INTO wishlists(id,owner_id,name,normalized_name,occasion,event_date,message,created_at,is_suspended,surprise_mode) VALUES($1,$2,$3,$4,$5,$6,$7,$8,false,COALESCE($9,true)) RETURNING `+wishlistColumns, security.NewID(), owner, name, strings.ToUpper(name), upperFirst(text(request.Occasion)), request.EventDate, optional(request.Message), s.Now().UTC(), request.SurpriseMode))
		if err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `INSERT INTO wishlist_group_visibility(wishlist_id) VALUES($1)`, r.Id)
		return err
	})
	if duplicate(err, "ux_wishlists_owner_normalized_name") {
		err = domain.Failure(409, domain.WishlistNameAlreadyExists, "A wishlist with this name already exists.")
	}
	return r, v, err
}

// UpdateWishlist replaces an owned list only when the caller's version is current.
func (s *Service) UpdateWishlist(ctx context.Context, owner, id string, request domain.UpdateWishlistRequest, version uint32) (domain.WishlistResponse, uint32, error) {
	var r domain.WishlistResponse
	var v uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, id, true); err != nil {
			return err
		}
		name := strings.TrimSpace(text(request.Name))
		var err error
		r, v, err = scanWishlist(tx.QueryRow(ctx, `SELECT `+wishlistColumns+` FROM wishlists WHERE id=$1 AND owner_id=$2`, id, owner))
		if err != nil {
			return err
		}
		if v != version {
			return stale(domain.WishlistVersionConflict)
		}
		updated, next, err := scanWishlist(tx.QueryRow(ctx, `UPDATE wishlists SET name=$3,normalized_name=$4,occasion=$5,event_date=$6,message=$7,updated_at=$8,surprise_mode=COALESCE($9,surprise_mode) WHERE id=$1 AND owner_id=$2 AND ROW(name,normalized_name,occasion,event_date,message,surprise_mode) IS DISTINCT FROM ROW($3,$4,$5,$6::date,$7,COALESCE($9,surprise_mode)) RETURNING `+wishlistColumns, id, owner, name, strings.ToUpper(name), upperFirst(text(request.Occasion)), request.EventDate, optional(request.Message), s.Now().UTC(), request.SurpriseMode))
		if errors.Is(err, pgx.ErrNoRows) {
			return nil
		}
		r, v = updated, next
		return err
	})
	if duplicate(err, "ux_wishlists_owner_normalized_name") {
		err = domain.Failure(409, domain.WishlistNameAlreadyExists, "A wishlist with this name already exists.")
	}
	return r, v, err
}

// DeleteWishlist physically removes an owned list and its dependent resources.
func (s *Service) DeleteWishlist(ctx context.Context, owner, id string, version uint32) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlistState(ctx, tx, owner, id, true, true); err != nil {
			return err
		}
		if err := s.queueWishlistImages(ctx, tx, id); err != nil {
			return err
		}
		result, err := tx.Exec(ctx, `DELETE FROM wishlists WHERE id=$1 AND owner_id=$2 AND xmin::text::bigint=$3`, id, owner, version)
		if err != nil {
			return err
		}
		if result.RowsAffected() == 0 {
			return stale(domain.WishlistVersionConflict)
		}
		return nil
	})
}

func (s *Service) lockWishlist(ctx context.Context, tx pgx.Tx, owner, id string, mutation bool) error {
	return s.lockWishlistState(ctx, tx, owner, id, mutation, false)
}

func (s *Service) lockWishlistState(ctx context.Context, tx pgx.Tx, owner, id string, mutation, allowArchived bool) error {
	// Account-before-parent locks fence mutations against concurrent account erasure.
	var member string
	if err := tx.QueryRow(ctx, `SELECT id::text FROM users WHERE id=$1 FOR KEY SHARE`, owner).Scan(&member); err != nil {
		if errors.Is(err, pgx.ErrNoRows) {
			return domain.Failure(401, domain.AccountAuthenticationSessionInvalid, "The authentication session is invalid.")
		}
		return err
	}
	var suspended, archived bool
	err := tx.QueryRow(ctx, `SELECT is_suspended,is_archived FROM wishlists WHERE id=$1 AND owner_id=$2 FOR UPDATE`, id, owner).Scan(&suspended, &archived)
	if errors.Is(err, pgx.ErrNoRows) {
		return missing(domain.WishlistNotFound)
	}
	if err != nil {
		return err
	}
	if mutation && suspended {
		return domain.Failure(409, domain.WishlistSuspended, "The wishlist is suspended.")
	}
	if mutation && archived && !allowArchived {
		return domain.Failure(409, domain.WishlistArchived, "The wishlist is archived.")
	}
	return nil
}

// SetWishlistArchived archives or restores a current owned list without revoking its shares.
func (s *Service) SetWishlistArchived(ctx context.Context, owner, id string, archived bool, version uint32) (domain.WishlistResponse, uint32, error) {
	var result domain.WishlistResponse
	var current uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlistState(ctx, tx, owner, id, true, true); err != nil {
			return err
		}
		var err error
		result, current, err = scanWishlist(tx.QueryRow(ctx, `SELECT `+wishlistColumns+` FROM wishlists WHERE id=$1 AND owner_id=$2`, id, owner))
		if err != nil {
			return err
		}
		if current != version {
			return stale(domain.WishlistVersionConflict)
		}
		if result.IsArchived == archived {
			return nil
		}
		result, current, err = scanWishlist(tx.QueryRow(ctx, `UPDATE wishlists SET is_archived=$3,updated_at=$4 WHERE id=$1 AND owner_id=$2 RETURNING `+wishlistColumns, id, owner, archived, s.Now().UTC()))
		return err
	})
	return result, current, err
}

func (s *Service) queueWishlistImages(ctx context.Context, tx pgx.Tx, id string) error {
	rows, err := tx.Query(ctx, `SELECT image_id::text FROM wishes WHERE wishlist_id=$1 AND image_id IS NOT NULL`, id)
	if err != nil {
		return err
	}
	ids, err := pgx.CollectRows(rows, pgx.RowTo[string])
	if err != nil {
		return err
	}
	for _, image := range ids {
		if err = s.queueImageDeletion(ctx, tx, image); err != nil {
			return err
		}
	}
	return nil
}

// EntityTag formats the stable lowercase hexadecimal representation of PostgreSQL xmin.
func EntityTag(version uint32) string { return fmt.Sprintf("\"%08x\"", version) }
