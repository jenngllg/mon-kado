package application

import (
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/jackc/pgx/v5"
)

// SharePreview contains only the explicitly public list title and bounded image material.
type SharePreview struct {
	Name      string
	HasImages bool
	JPEG      []byte
}

// PublicSharePreview reads current public metadata without exposing a sharing capability.
func (s *Service) PublicSharePreview(ctx context.Context, link string, withImage bool) (SharePreview, error) {
	var result SharePreview
	var contents [][]byte
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var wishlist string
		err := tx.QueryRow(ctx, `SELECT w.id::text,w.name FROM wishlists w JOIN wishlist_share_links l ON l.wishlist_id=w.id JOIN users u ON u.id=w.owner_id WHERE l.id=$1 AND NOT w.is_archived AND NOT w.is_suspended AND u.email_confirmed FOR SHARE OF w,l,u`, link).Scan(&wishlist, &result.Name)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.SharedWishlistNotFound)
		}
		if err != nil {
			return err
		}
		rows, err := tx.Query(ctx, `SELECT image_id::text,image_content_hash FROM wishes WHERE wishlist_id=$1 AND image_id IS NOT NULL ORDER BY position,id LIMIT 2 FOR SHARE`, wishlist)
		if err != nil {
			return err
		}
		defer rows.Close()
		for rows.Next() {
			var id string
			var hash []byte
			if err = rows.Scan(&id, &hash); err != nil {
				return err
			}
			result.HasImages = true
			if !withImage {
				continue
			}
			content, err := images.Read(ctx, s.Config.ImagePath, id)
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
			contents = append(contents, content)
		}
		return rows.Err()
	})
	if err != nil {
		return result, err
	}
	if withImage {
		if !result.HasImages {
			return result, missing(domain.WishImageNotFound)
		}
		result.JPEG, err = images.ComposePreview(ctx, contents)
	}
	return result, err
}
