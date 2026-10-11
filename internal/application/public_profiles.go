package application

import (
	"context"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/jackc/pgx/v5"
)

// PublicMemberProfile returns only a confirmed member's public identity and discoverable lists.
func (s *Service) PublicMemberProfile(ctx context.Context, member string) (domain.PublicMemberProfileResponse, error) {
	result := domain.PublicMemberProfileResponse{Wishlists: []domain.PublicMemberWishlistResponse{}}
	tx, err := s.Store.Pool.BeginTx(ctx, pgx.TxOptions{IsoLevel: pgx.RepeatableRead, AccessMode: pgx.ReadOnly})
	if err != nil {
		return result, err
	}
	defer tx.Rollback(ctx)
	var image *string
	err = tx.QueryRow(ctx, `SELECT id::text,display_name,profile_image_id::text FROM users WHERE id=$1 AND email_confirmed`, member).Scan(&result.Id, &result.DisplayName, &image)
	if errors.Is(err, pgx.ErrNoRows) {
		return result, missing(domain.PublicMemberProfileNotFound)
	}
	if err != nil {
		return result, err
	}
	if image != nil {
		result.ProfileImageUrl = ptrString("/api/v1/members/" + member + "/profile/image?imageId=" + *image)
	}
	rows, err := tx.Query(ctx, `SELECT w.id::text,w.name,w.occasion,w.event_date::text,l.id::text,l.protected_secret FROM wishlists w JOIN wishlist_share_links l ON l.wishlist_id=w.id WHERE w.owner_id=$1 AND NOT w.is_suspended AND NOT w.is_archived ORDER BY w.created_at DESC,w.id`, member)
	if err != nil {
		return result, err
	}
	defer rows.Close()
	for rows.Next() {
		var list domain.PublicMemberWishlistResponse
		var link, protected string
		if err = rows.Scan(&list.Id, &list.Name, &list.Occasion, &list.EventDate, &link, &protected); err != nil {
			return result, err
		}
		secret, err := s.Crypto.Unprotect(protected, "share|"+link)
		if err != nil {
			return result, err
		}
		list.Occasion = lowerFirst(list.Occasion)
		list.ShareUrl = s.Config.FrontendOrigin + "/shared-wishlists/" + link + "#" + secret
		result.Wishlists = append(result.Wishlists, list)
	}
	if err = rows.Err(); err != nil {
		return result, err
	}
	rows.Close()
	return result, tx.Commit(ctx)
}
