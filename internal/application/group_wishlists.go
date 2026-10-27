package application

import (
	"context"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

const groupWishlistSummary = `SELECT w.id::text,w.name,w.owner_id::text,u.display_name,w.occasion,w.event_date::text,gw.created_at FROM visible_group_wishlists gw JOIN wishlists w ON w.id=gw.wishlist_id JOIN users u ON u.id=w.owner_id WHERE gw.group_id=$1 AND NOT w.is_archived AND NOT w.is_suspended`

func scanGroupWishlist(row pgx.Row) (domain.GroupWishlistResponse, error) {
	var r domain.GroupWishlistResponse
	err := row.Scan(&r.Id, &r.Name, &r.OwnerId, &r.OwnerDisplayName, &r.Occasion, &r.EventDate, &r.CreatedAt)
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.GroupWishlistNotFound)
	}
	r.Occasion = lowerFirst(r.Occasion)
	return r, err
}

// GroupWishlists lists only actively shared content of current group members.
func (s *Service) GroupWishlists(ctx context.Context, member, group string, page, size int) (domain.PaginatedResponse[domain.GroupWishlistResponse], error) {
	var result domain.PaginatedResponse[domain.GroupWishlistResponse]
	err := s.groupReadTransaction(ctx, func(tx pgx.Tx) error {
		if _, _, err := s.lockGroup(ctx, tx, member, group, false); err != nil {
			return err
		}
		var err error
		result, err = groupPage(ctx, tx, groupWishlistSummary, "gw.created_at DESC,w.id", []any{group}, page, size, scanGroupWishlist)
		return err
	})
	return result, err
}

// ShareGroupWishlist grants or withdraws access only for an owned list, without public sharing.
func (s *Service) ShareGroupWishlist(ctx context.Context, member, group, wishlist string, remove bool) (domain.GroupWishlistResponse, error) {
	var result domain.GroupWishlistResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, member, wishlist, !remove); err != nil {
			return err
		}
		// Match account erasure's wishlist-before-group order to avoid lock cycles.
		if _, _, err := s.lockGroup(ctx, tx, member, group, false); err != nil {
			return err
		}
		var visible bool
		if err := tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM visible_group_wishlists WHERE group_id=$1 AND wishlist_id=$2)`, group, wishlist).Scan(&visible); err != nil {
			return err
		}
		if remove {
			if !visible {
				return missing(domain.GroupWishlistNotFound)
			}
			if _, err := tx.Exec(ctx, `DELETE FROM group_wishlists WHERE group_id=$1 AND wishlist_id=$2 AND owner_id=$3`, group, wishlist, member); err != nil {
				return err
			}
			if _, err := tx.Exec(ctx, `INSERT INTO group_wishlist_exclusions(group_id,wishlist_id,owner_id,created_at) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING`, group, wishlist, member, s.Now().UTC()); err != nil {
				return err
			}
			return s.touchWishlistGroupVisibility(ctx, tx, wishlist)
		}
		if visible {
			return domain.Failure(409, domain.GroupWishlistAlreadyExists, "The wishlist is already shared with this group.")
		}
		if _, err := tx.Exec(ctx, `DELETE FROM group_wishlist_exclusions WHERE group_id=$1 AND wishlist_id=$2`, group, wishlist); err != nil {
			return err
		}
		_, err := tx.Exec(ctx, `INSERT INTO group_wishlists(group_id,wishlist_id,owner_id,created_at)VALUES($1,$2,$3,$4)`, group, wishlist, member, s.Now().UTC())
		if duplicate(err, "group_wishlists_pkey") {
			return domain.Failure(409, domain.GroupWishlistAlreadyExists, "The wishlist is already shared with this group.")
		}
		if err != nil {
			return err
		}
		if err = s.touchWishlistGroupVisibility(ctx, tx, wishlist); err != nil {
			return err
		}
		result, err = scanGroupWishlist(tx.QueryRow(ctx, groupWishlistSummary+` AND w.id=$2`, group, wishlist))
		return err
	})
	return result, err
}

func (s *Service) verifyGroupWishlist(ctx context.Context, tx pgx.Tx, member, group, wishlist string) (verifiedShare, error) {
	share := verifiedShare{Group: group, Member: member}
	// Never lock a foreign private list merely because its UUID is known.
	// Lock the wishlist first, consistently with account erasure and public reservations.
	err := tx.QueryRow(ctx, `SELECT w.id::text,w.owner_id::text,w.name,w.surprise_mode FROM visible_group_wishlists gw JOIN wishlists w ON w.id=gw.wishlist_id WHERE gw.group_id=$1 AND w.id=$2 AND EXISTS(SELECT 1 FROM group_members gm WHERE gm.group_id=gw.group_id AND gm.member_id=$3) AND NOT w.is_archived AND NOT w.is_suspended FOR UPDATE OF w`, group, wishlist, member).Scan(&share.Wishlist, &share.Owner, &share.Name, &share.SurpriseMode)
	if errors.Is(err, pgx.ErrNoRows) {
		return share, missing(domain.GroupWishlistNotFound)
	}
	if err != nil {
		return share, err
	}
	if _, _, err = s.lockGroup(ctx, tx, member, group, false); err != nil {
		return share, err
	}
	// A withdrawal may have committed while the wishlist lock was awaited.
	// Re-read the grant under the group lock rather than trusting the first snapshot.
	var granted bool
	if err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM visible_group_wishlists WHERE group_id=$1 AND wishlist_id=$2)`, group, wishlist).Scan(&granted); err != nil {
		return share, err
	}
	if !granted {
		return share, missing(domain.GroupWishlistNotFound)
	}
	return share, err
}

// GroupWishlist reads a shared list, preserving the owner's configured reservation privacy.
func (s *Service) GroupWishlist(ctx context.Context, member, group, wishlist string, availableOnly bool) (domain.SharedWishlistResponse, error) {
	r := domain.SharedWishlistResponse{Wishes: []domain.SharedWishResponse{}}
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyGroupWishlist(ctx, tx, member, group, wishlist)
		if err != nil {
			return err
		}
		r.CanReserve = share.Owner != member
		r.CanSubscribe = share.Owner != member
		if err = tx.QueryRow(ctx, `SELECT w.id::text,w.owner_id::text,u.display_name,w.name,w.occasion,w.event_date::text,w.message FROM wishlists w JOIN users u ON u.id=w.owner_id WHERE w.id=$1`, wishlist).Scan(&r.Id, &r.OwnerId, &r.OwnerDisplayName, &r.Name, &r.Occasion, &r.EventDate, &r.Message); err != nil {
			return err
		}
		r.Occasion = lowerFirst(r.Occasion)
		if r.CanReserve {
			r.CurrentParticipant, err = s.participant(ctx, tx, share, member, "")
			if err != nil {
				return err
			}
		}
		rows, err := tx.Query(ctx, `SELECT id::text FROM wishes WHERE wishlist_id=$1 ORDER BY position`, wishlist)
		if err != nil {
			return err
		}
		ids, err := pgx.CollectRows(rows, pgx.RowTo[string])
		if err != nil {
			return err
		}
		for _, id := range ids {
			wish, err := s.sharedWish(ctx, tx, share, "", id, r.CurrentParticipant)
			if err != nil {
				return err
			}
			if availableOnly && wish.AvailableQuantity != nil && *wish.AvailableQuantity == 0 && (wish.CurrentParticipantReservedQuantity == nil || *wish.CurrentParticipantReservedQuantity == 0) {
				continue
			}
			r.Wishes = append(r.Wishes, domain.SharedWishResponse{Id: wish.Id, Name: wish.Name, Note: wish.Note, Url: wish.Url, Price: wish.Price, Quantity: wish.Quantity, ReservedQuantity: wish.ReservedQuantity, AvailableQuantity: wish.AvailableQuantity, CurrentParticipantReservedQuantity: wish.CurrentParticipantReservedQuantity, ImageUrl: wish.ImageUrl, IsFavorite: wish.IsFavorite, Color: wish.Color, Size: wish.Size, IsSurprise: wish.IsSurprise, AcceptsSecondHand: wish.AcceptsSecondHand})
		}
		return nil
	})
	return r, err
}

// GroupWish reads one child only while current membership and explicit sharing remain valid.
func (s *Service) GroupWish(ctx context.Context, member, group, wishlist, wish string) (domain.SharedWishDetailResponse, error) {
	var result domain.SharedWishDetailResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyGroupWishlist(ctx, tx, member, group, wishlist)
		if err != nil {
			return err
		}
		p, err := s.participant(ctx, tx, share, member, "")
		if err != nil {
			return err
		}
		result, err = s.sharedWish(ctx, tx, share, "", wish, p)
		return err
	})
	return result, err
}

// GroupReservation uses the same participant and capacity locks as public and owner reservations.
// Only an explicit PUT can enroll a member in the list; page reads are side-effect free.
func (s *Service) GroupReservation(ctx context.Context, member, group, wishlist, wish, method string, quantity int, expected *uint32) (ReservationResult, error) {
	var result ReservationResult
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyGroupWishlist(ctx, tx, member, group, wishlist)
		if err != nil {
			return err
		}
		if share.Owner == member {
			return domain.Failure(409, domain.GroupOwnerWishlist, "The owner cannot reserve through the group view.")
		}
		p, err := s.participant(ctx, tx, share, member, "")
		if err != nil {
			return err
		}
		if p == nil {
			if method != "PUT" {
				return missing(domain.GiftReservationNotFound)
			}
			if expected != nil {
				return stale(domain.GiftReservationVersionConflict)
			}
			var count int
			if err = tx.QueryRow(ctx, `SELECT count(*) FROM wishlist_participants WHERE wishlist_id=$1 AND (member_id IS NOT NULL OR guest_session_id IS NOT NULL)`, wishlist).Scan(&count); err != nil {
				return err
			}
			if count >= wishlistParticipantLimit {
				return domain.Failure(409, domain.WishlistParticipantLimitReached, "The wishlist has reached its participant limit.")
			}
			p = &domain.WishlistParticipantResponse{Id: security.NewID()}
			if _, err = tx.Exec(ctx, `INSERT INTO wishlist_participants(id,wishlist_id,member_id,guest_display_name,created_at) SELECT $1,$2,id,display_name,$4 FROM users WHERE id=$3`, p.Id, wishlist, member, s.Now().UTC()); err != nil {
				return err
			}
		}
		return s.mutateReservation(ctx, tx, share, wish, member, p.Id, method, quantity, expected, &result)
	})
	return result, err
}
