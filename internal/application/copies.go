package application

import (
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// CopyWish creates an independent owned wish from an owned or capability-authorized source.
func (s *Service) CopyWish(ctx context.Context, member, destination, source, wish, link, secret string) (domain.WishResponse, uint32, error) {
	var result domain.WishResponse
	var version uint32
	var staged string
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var account string
		if err := tx.QueryRow(ctx, `SELECT id::text FROM users WHERE id=$1 FOR KEY SHARE`, member).Scan(&account); err != nil {
			if errors.Is(err, pgx.ErrNoRows) {
				return unauthorized()
			}
			return err
		}
		if link != "" {
			if err := tx.QueryRow(ctx, `SELECT wishlist_id::text FROM wishlist_share_links WHERE id=$1`, link).Scan(&source); err != nil {
				if errors.Is(err, pgx.ErrNoRows) {
					return missing(domain.SharedWishlistNotFound)
				}
				return err
			}
		}
		// Both parents use one order, including reciprocal copies, to prevent deadlocks.
		rows, err := tx.Query(ctx, `SELECT id FROM wishlists WHERE id=$1 OR id=$2 ORDER BY id FOR UPDATE`, source, destination)
		if err != nil {
			return err
		}
		for rows.Next() {
		}
		err = rows.Err()
		rows.Close()
		if err != nil {
			return err
		}
		if err = s.lockWishlist(ctx, tx, member, destination, true); err != nil {
			return err
		}
		missingWish := domain.WishNotFound
		if link == "" {
			if err = s.lockWishlist(ctx, tx, member, source, true); err != nil {
				return err
			}
		} else {
			share, err := s.verifyShare(ctx, tx, link, secret)
			if err != nil {
				return err
			}
			if share.Wishlist != source {
				return missing(domain.SharedWishlistNotFound)
			}
			missingWish = domain.SharedWishNotFound
		}
		var name string
		var note, color, size, address, price, image *string
		var quantity int
		var hash []byte
		err = tx.QueryRow(ctx, `SELECT name,note,color,size,url,price::text,quantity,image_id::text,image_content_hash FROM wishes WHERE wishlist_id=$1 AND id=$2 FOR UPDATE`, source, wish).Scan(&name, &note, &color, &size, &address, &price, &quantity, &image, &hash)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(missingWish)
		}
		if err != nil {
			return err
		}
		var position int64
		var count int
		if err = tx.QueryRow(ctx, `SELECT next_position,current_count FROM wish_position_sequences WHERE wishlist_id=$1 FOR UPDATE`, destination).Scan(&position, &count); err != nil {
			return err
		}
		if count >= 1000 {
			return domain.Failure(409, domain.WishLimitReached, "The wishlist has reached its wish limit.")
		}
		var copiedImage *string
		if image != nil {
			content, err := images.Read(ctx, s.Config.ImagePath, *image)
			if err != nil {
				if ctx.Err() != nil {
					return ctx.Err()
				}
				return domain.Failure(503, domain.TechnicalServiceUnavailable, "Image storage is unavailable.")
			}
			digest := sha256.Sum256(content)
			if subtle.ConstantTimeCompare(digest[:], hash) != 1 {
				return domain.Failure(503, domain.TechnicalServiceUnavailable, "Image storage is unavailable.")
			}
			staged = security.NewID()
			if err = images.WritePending(ctx, s.Config.ImagePath, staged, content); err != nil {
				return err
			}
			copiedImage = &staged
		}
		if _, err = tx.Exec(ctx, `UPDATE wish_position_sequences SET next_position=$2 WHERE wishlist_id=$1`, destination, position+1); err != nil {
			return err
		}
		result, version, err = s.scanWish(member, tx.QueryRow(ctx, `INSERT INTO wishes(id,wishlist_id,name,note,url,price,quantity,position,created_at,image_id,image_content_hash,color,size) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13) RETURNING `+wishColumns, security.NewID(), destination, name, note, address, price, quantity, position+1, s.Now().UTC(), copiedImage, hash, color, size))
		return err
	})
	if staged != "" {
		if err != nil {
			s.removeUnreferencedImage(ctx, staged)
		} else {
			images.Finalize(s.Config.ImagePath, staged)
		}
	}
	return result, version, err
}
