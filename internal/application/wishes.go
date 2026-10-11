package application

import (
	"context"
	"errors"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
)

const wishColumns = `id::text,wishlist_id::text,name,note,url,price::text,quantity,position,created_at,updated_at,image_id::text,xmin::text::bigint`

func (s *Service) scanWish(owner string, row pgx.Row) (domain.WishResponse, uint32, error) {
	var r domain.WishResponse
	var v uint32
	var image *string
	err := row.Scan(&r.Id, &r.WishlistId, &r.Name, &r.Note, &r.Url, &r.Price, &r.Quantity, &r.Position, &r.CreatedAt, &r.UpdatedAt, &image, &v)
	if image != nil {
		u, urlErr := s.imageURL(owner, "", r.WishlistId, r.Id, *image)
		if urlErr != nil {
			return r, v, urlErr
		}
		r.ImageUrl = &u
	}
	return r, v, err
}

// Wishes returns the complete ordered collection and the collection ETag.
func (s *Service) Wishes(ctx context.Context, owner, wishlist string) (domain.WishCollectionResponse, uint32, error) {
	r := domain.WishCollectionResponse{Wishes: []domain.WishCollectionItemResponse{}}
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, false); err != nil {
			return err
		}
		if err := tx.QueryRow(ctx, `SELECT xmin::text::bigint FROM wish_position_sequences WHERE wishlist_id=$1`, wishlist).Scan(&version); err != nil {
			return err
		}
		rows, err := tx.Query(ctx, `SELECT `+wishColumns+` FROM wishes WHERE wishlist_id=$1 ORDER BY position`, wishlist)
		if err != nil {
			return err
		}
		defer rows.Close()
		for rows.Next() {
			wish, v, err := s.scanWish(owner, rows)
			if err != nil {
				return err
			}
			r.Wishes = append(r.Wishes, domain.WishCollectionItemResponse{Id: wish.Id, WishlistId: wish.WishlistId, Name: wish.Name, Note: wish.Note, Url: wish.Url, Price: wish.Price, Quantity: wish.Quantity, ImageUrl: wish.ImageUrl, Position: wish.Position, CreatedAt: wish.CreatedAt, UpdatedAt: wish.UpdatedAt, EntityTag: EntityTag(v)})
		}
		return rows.Err()
	})
	return r, version, err
}

// Wish retrieves a wish using both its parent and its identifier.
func (s *Service) Wish(ctx context.Context, owner, wishlist, id string) (domain.WishResponse, uint32, error) {
	if err := s.AuthorizeWishlist(ctx, owner, wishlist, false); err != nil {
		return domain.WishResponse{}, 0, err
	}
	r, v, err := s.scanWish(owner, s.Store.Pool.QueryRow(ctx, `SELECT `+wishColumns+` FROM wishes WHERE wishlist_id=$1 AND id=$2`, wishlist, id))
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.WishNotFound)
	}
	return r, v, err
}

// CreateWish atomically allocates a position and enforces the list's 1000-wish limit.
func (s *Service) CreateWish(ctx context.Context, owner, wishlist string, request domain.CreateWishRequest) (domain.WishResponse, uint32, error) {
	var r domain.WishResponse
	var v uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		var position int64
		var count int
		if err := tx.QueryRow(ctx, `SELECT next_position,current_count FROM wish_position_sequences WHERE wishlist_id=$1 FOR UPDATE`, wishlist).Scan(&position, &count); err != nil {
			return err
		}
		if count >= 1000 {
			return domain.Failure(409, domain.WishLimitReached, "The wishlist has reached its wish limit.")
		}
		if _, err := tx.Exec(ctx, `UPDATE wish_position_sequences SET next_position=$2 WHERE wishlist_id=$1`, wishlist, position+1); err != nil {
			return err
		}
		var err error
		r, v, err = s.scanWish(owner, tx.QueryRow(ctx, `INSERT INTO wishes(id,wishlist_id,name,note,url,price,quantity,position,created_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9) RETURNING `+wishColumns, security.NewID(), wishlist, strings.TrimSpace(text(request.Name)), optional(request.Note), optional(request.Url), request.Price, quantity(request.Quantity), position+1, s.Now().UTC()))
		return err
	})
	return r, v, err
}

// UpdateWish replaces editable fields, preserving reserved-quantity invariants.
func (s *Service) UpdateWish(ctx context.Context, owner, wishlist, id string, request domain.UpdateWishRequest, version uint32) (domain.WishResponse, uint32, error) {
	var r domain.WishResponse
	var v uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		var current uint32
		err := tx.QueryRow(ctx, `SELECT xmin::text::bigint FROM wishes WHERE id=$1 AND wishlist_id=$2 FOR UPDATE`, id, wishlist).Scan(&current)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.WishNotFound)
		}
		if err != nil {
			return err
		}
		if current != version {
			return stale(domain.WishVersionConflict)
		}
		r, v, err = s.scanWish(owner, tx.QueryRow(ctx, `UPDATE wishes SET name=$3,note=$4,url=$5,price=$6,quantity=$7,updated_at=$8 WHERE id=$1 AND wishlist_id=$2 RETURNING `+wishColumns, id, wishlist, strings.TrimSpace(text(request.Name)), optional(request.Note), optional(request.Url), request.Price, quantity(request.Quantity), s.Now().UTC()))
		return err
	})
	var pgError *pgconn.PgError
	if errors.As(err, &pgError) && pgError.ConstraintName == "ck_wishes_quantity_not_below_reserved" {
		err = domain.Failure(409, domain.WishQuantityBelowReserved, "The desired quantity cannot be below the reserved quantity.")
	}
	return r, v, err
}

// DeleteWish deletes one current version and queues image removal in the same transaction.
func (s *Service) DeleteWish(ctx context.Context, owner, wishlist, id string, version uint32) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		var current uint32
		var image *string
		err := tx.QueryRow(ctx, `SELECT xmin::text::bigint,image_id::text FROM wishes WHERE id=$1 AND wishlist_id=$2 FOR UPDATE`, id, wishlist).Scan(&current, &image)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.WishNotFound)
		}
		if err != nil {
			return err
		}
		if current != version {
			return stale(domain.WishVersionConflict)
		}
		if image != nil {
			if err = s.queueImageDeletion(ctx, tx, *image); err != nil {
				return err
			}
		}
		_, err = tx.Exec(ctx, `DELETE FROM wishes WHERE id=$1 AND wishlist_id=$2`, id, wishlist)
		return err
	})
}

func (s *Service) queueImageDeletion(ctx context.Context, tx pgx.Tx, id string) error {
	now := s.Now().UTC()
	_, err := tx.Exec(ctx, `INSERT INTO gift_image_deletion_outbox(id,image_id,created_at,available_at,attempt_count) VALUES($1,$2,$3,$3,0)`, security.NewID(), id, now)
	return err
}

// ReorderWishes accepts an exact permutation and checks the collection version atomically.
func (s *Service) ReorderWishes(ctx context.Context, owner, wishlist string, ids []string, version uint32) (domain.WishOrderResponse, uint32, error) {
	r := domain.WishOrderResponse{Wishes: []domain.WishOrderItemResponse{}}
	var resultVersion uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		var current uint32
		var count int
		var maxPosition int64
		if err := tx.QueryRow(ctx, `SELECT xmin::text::bigint,current_count,next_position FROM wish_position_sequences WHERE wishlist_id=$1 FOR UPDATE`, wishlist).Scan(&current, &count, &maxPosition); err != nil {
			return err
		}
		if current != version {
			return stale(domain.WishOrderVersionConflict)
		}
		if len(ids) != count {
			return domain.Failure(409, domain.WishOrderConflict, "The order must include every wish exactly once.")
		}
		rows, err := tx.Query(ctx, `SELECT id::text,position,xmin::text::bigint FROM wishes WHERE wishlist_id=$1 ORDER BY position,id FOR UPDATE`, wishlist)
		if err != nil {
			return err
		}
		original := map[string]domain.WishOrderItemResponse{}
		positions := make([]int64, 0, count)
		for rows.Next() {
			var item domain.WishOrderItemResponse
			var v uint32
			if err = rows.Scan(&item.Id, &item.Position, &v); err != nil {
				rows.Close()
				return err
			}
			item.EntityTag = EntityTag(v)
			original[item.Id] = item
			positions = append(positions, item.Position)
		}
		rows.Close()
		if err = rows.Err(); err != nil {
			return err
		}
		ordered := make([]string, len(ids))
		seen := map[string]bool{}
		for i, id := range ids {
			parsed, parseErr := uuid.Parse(id)
			canonical := parsed.String()
			if _, found := original[canonical]; parseErr != nil || !found || seen[canonical] {
				return domain.Failure(409, domain.WishOrderConflict, "The order must include every wish exactly once.")
			}
			seen[canonical] = true
			ordered[i] = canonical
		}
		for i, id := range ordered {
			if original[id].Position == positions[i] {
				continue
			}
			if _, err := tx.Exec(ctx, `UPDATE wishes SET position=$3 WHERE id=$1 AND wishlist_id=$2`, id, wishlist, maxPosition+int64(i)+1); err != nil {
				return err
			}
		}
		for i, id := range ordered {
			item := original[id]
			if item.Position == positions[i] {
				r.Wishes = append(r.Wishes, item)
				continue
			}
			var v uint32
			if err := tx.QueryRow(ctx, `UPDATE wishes SET position=$3,updated_at=$4 WHERE id=$1 AND wishlist_id=$2 RETURNING xmin::text::bigint`, id, wishlist, positions[i], s.Now().UTC()).Scan(&v); err != nil {
				return err
			}
			r.Wishes = append(r.Wishes, domain.WishOrderItemResponse{Id: id, Position: positions[i], EntityTag: EntityTag(v)})
		}
		return tx.QueryRow(ctx, `SELECT xmin::text::bigint FROM wish_position_sequences WHERE wishlist_id=$1`, wishlist).Scan(&resultVersion)
	})
	return r, resultVersion, err
}
