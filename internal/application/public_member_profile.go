package application

import (
	"context"

	"github.com/JennGllg/mon-kado/internal/domain"
)

// PublicMemberProfile reads a confirmed identity and its current discoverable links in one snapshot.
// Search opt-out does not revoke the public profile or an already shared list.
func (s *Service) PublicMemberProfile(ctx context.Context, memberID string) (domain.PublicMemberProfileResponse, error) {
	result := domain.PublicMemberProfileResponse{Wishlists: []domain.PublicMemberWishlistResponse{}}
	rows, err := s.Store.Pool.Query(ctx, `
		SELECT u.id::text,u.display_name,u.display_name_tag,u.profile_image_id::text,
		       w.id::text,w.name,w.occasion,w.event_date::text,l.id::text,l.protected_secret
		FROM users u
		LEFT JOIN (wishlists w INNER JOIN wishlist_share_links l ON l.wishlist_id=w.id)
		    ON w.owner_id=u.id AND NOT w.is_suspended AND NOT w.is_archived
		WHERE u.id=$1 AND u.email_confirmed
		ORDER BY w.created_at DESC,w.id ASC`, memberID)
	if err != nil {
		return result, err
	}
	defer rows.Close()
	for rows.Next() {
		var imageID, wishlistID, name, occasion, eventDate, linkID, protected *string
		if err = rows.Scan(&result.Id, &result.DisplayName, &result.DisplayNameTag, &imageID,
			&wishlistID, &name, &occasion, &eventDate, &linkID, &protected); err != nil {
			return result, err
		}
		if imageID != nil {
			result.ProfileImageUrl = ptrString("/api/v1/members/" + result.Id + "/profile/image?imageId=" + *imageID)
		}
		if wishlistID == nil {
			continue
		}
		secret, err := s.Crypto.Unprotect(*protected, "share|"+*linkID)
		if err != nil {
			return result, err
		}
		result.Wishlists = append(result.Wishlists, domain.PublicMemberWishlistResponse{
			Id: *wishlistID, Name: *name, Occasion: lowerFirst(*occasion), EventDate: eventDate,
			ShareUrl: s.Config.FrontendOrigin + "/shared-wishlists/" + *linkID + "#" + secret,
		})
	}
	if err = rows.Err(); err != nil {
		return result, err
	}
	if result.Id == "" {
		return result, missing(domain.AccountPublicProfileNotFound)
	}
	return result, nil
}
