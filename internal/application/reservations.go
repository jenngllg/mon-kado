package application

import (
	"context"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// ReservationResult contains the public resource and its concurrent version.
type ReservationResult struct {
	Reservation domain.GiftReservationResponse
	Version     uint32
	Created     bool
}

// Reservation serializes absolute quantities against the wished quantity and optimistic ETag.
func (s *Service) Reservation(ctx context.Context, link, secret, wishID, member, guest, method string, quantity int, expected *uint32) (ReservationResult, error) {
	var result ReservationResult
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyShare(ctx, tx, link, secret)
		if err != nil {
			return err
		}
		participant, err := s.participant(ctx, tx, share, member, guest)
		if err != nil {
			return err
		}
		if participant == nil {
			if member == "" && guest == "" {
				return domain.Failure(401, domain.GuestSessionInvalid, "A participant identity is required.")
			}
			return missing(domain.WishlistParticipantNotFound)
		}
		return s.reservationInTransaction(ctx, tx, share, wishID, participant.Id, member, method, quantity, expected, domain.SharedWishNotFound, &result)
	})
	return result, err
}

// OwnerReservation manages only the owner's reservation, independently of public sharing.
// Other participants' quantities are used to enforce capacity but are never returned.
func (s *Service) OwnerReservation(ctx context.Context, owner, wishlist, wishID, method string, quantity int, expected *uint32) (ReservationResult, error) {
	var result ReservationResult
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		share := verifiedShare{Wishlist: wishlist, Owner: owner}
		if err := tx.QueryRow(ctx, `SELECT name FROM wishlists WHERE id=$1`, wishlist).Scan(&share.Name); err != nil {
			return err
		}
		var exists bool
		if err := tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM wishes WHERE wishlist_id=$1 AND id=$2)`, wishlist, wishID).Scan(&exists); err != nil {
			return err
		}
		if !exists {
			return missing(domain.WishNotFound)
		}
		participant, err := s.participant(ctx, tx, share, owner, "")
		if err != nil {
			return err
		}
		if participant == nil {
			if method != "PUT" {
				return missing(domain.GiftReservationNotFound)
			}
			if expected != nil {
				return stale(domain.GiftReservationVersionConflict)
			}
			// A private reservation must not create or renew a public share link.
			participant = &domain.WishlistParticipantResponse{Id: security.NewID()}
			_, err = tx.Exec(ctx, `INSERT INTO wishlist_participants(id,wishlist_id,member_id,guest_display_name,created_at) SELECT $1,$2,id,display_name,$4 FROM users WHERE id=$3`, participant.Id, wishlist, owner, s.Now().UTC())
			if err != nil {
				return err
			}
		}
		return s.reservationInTransaction(ctx, tx, share, wishID, participant.Id, owner, method, quantity, expected, domain.WishNotFound, &result)
	})
	return result, err
}

func (s *Service) reservationInTransaction(ctx context.Context, tx pgx.Tx, share verifiedShare, wishID, participantID, member, method string, quantity int, expected *uint32, notFound string, result *ReservationResult) error {
	var desired int
	var wishName string
	err := tx.QueryRow(ctx, `SELECT quantity,name FROM wishes WHERE wishlist_id=$1 AND id=$2 FOR UPDATE`, share.Wishlist, wishID).Scan(&desired, &wishName)
	if errors.Is(err, pgx.ErrNoRows) {
		return missing(notFound)
	}
	if err != nil {
		return err
	}
	r := &result.Reservation
	err = tx.QueryRow(ctx, `SELECT id::text,wish_id::text,quantity,created_at,updated_at,xmin::text::bigint FROM gift_reservations WHERE wishlist_id=$1 AND wish_id=$2 AND wishlist_participant_id=$3 FOR UPDATE`, share.Wishlist, wishID, participantID).Scan(&r.Id, &r.WishId, &r.Quantity, &r.CreatedAt, &r.UpdatedAt, &result.Version)
	exists := err == nil
	if err != nil && !errors.Is(err, pgx.ErrNoRows) {
		return err
	}
	if method != "PUT" && !exists {
		return missing(domain.GiftReservationNotFound)
	}
	if method == "GET" {
		return nil
	}
	if exists && expected == nil {
		return domain.Failure(428, domain.RequestPreconditionRequired, "The If-Match header is required.")
	}
	if expected != nil && (!exists || *expected != result.Version) {
		return stale(domain.GiftReservationVersionConflict)
	}
	now := s.Now().UTC()
	if method == "DELETE" {
		if member != "" {
			if _, err = tx.Exec(ctx, `UPDATE gift_reservation_histories SET status='Cancelled',ended_at=$2,last_activity_at=$2 WHERE id=$1`, r.Id, now); err != nil {
				return err
			}
		}
		_, err = tx.Exec(ctx, `DELETE FROM gift_reservations WHERE id=$1`, r.Id)
		return err
	}
	var reserved int
	if err = tx.QueryRow(ctx, `SELECT COALESCE(sum(quantity),0) FROM gift_reservations WHERE wish_id=$1 AND wishlist_participant_id<>$2`, wishID, participantID).Scan(&reserved); err != nil {
		return err
	}
	if quantity+reserved > desired {
		return domain.Failure(409, domain.GiftReservationQuantityUnavailable, "The requested quantity is no longer available.")
	}
	if exists && r.Quantity == quantity {
		return nil
	}
	if !exists {
		r.Id = security.NewID()
		err = tx.QueryRow(ctx, `INSERT INTO gift_reservations(id,wishlist_id,wish_id,wishlist_participant_id,quantity,created_at) VALUES($1,$2,$3,$4,$5,$6) RETURNING wish_id::text,quantity,created_at,updated_at,xmin::text::bigint`, r.Id, share.Wishlist, wishID, participantID, quantity, now).Scan(&r.WishId, &r.Quantity, &r.CreatedAt, &r.UpdatedAt, &result.Version)
		result.Created = true
	} else {
		err = tx.QueryRow(ctx, `UPDATE gift_reservations SET quantity=$2,updated_at=$3 WHERE id=$1 RETURNING wish_id::text,quantity,created_at,updated_at,xmin::text::bigint`, r.Id, quantity, now).Scan(&r.WishId, &r.Quantity, &r.CreatedAt, &r.UpdatedAt, &result.Version)
	}
	if err != nil {
		return err
	}
	if member != "" {
		_, err = tx.Exec(ctx, `INSERT INTO gift_reservation_histories(id,member_id,wishlist_id,wishlist_name,wish_id,wish_name,quantity,status,created_at,last_activity_at,is_group_reservation,author_id,author_display_name) SELECT $1,$2,$3,$4,$5,$6,$7,'Active',$8,$9,$10,u.id,u.display_name FROM users u WHERE u.id=$11 ON CONFLICT(id) DO UPDATE SET quantity=EXCLUDED.quantity,last_activity_at=EXCLUDED.last_activity_at`, r.Id, member, share.Wishlist, share.Name, wishID, wishName, quantity, r.CreatedAt, now, share.Group != "", share.Owner)
	}
	if err == nil && !exists && member != share.Owner {
		var surprise bool
		if err = tx.QueryRow(ctx, `SELECT surprise_mode FROM wishlists WHERE id=$1`, share.Wishlist).Scan(&surprise); err != nil {
			return err
		}
		if !surprise {
			return s.notify(ctx, tx, share.Owner, notificationEvent{kind: "wishReserved", actor: member, participant: participantID, wishlist: share.Wishlist, subject: wishID, key: "reserved:" + r.Id})
		}
	}
	return err
}

// suggestionHistoryVisible excludes private suggestion history whenever its
// original group grant, list or membership is no longer active.
const suggestionHistoryVisible = `(h.suggestion_group_id IS NULL OR EXISTS (
 SELECT 1 FROM group_suggestions gs
 JOIN wishlists sw ON sw.id=gs.wishlist_id
 JOIN visible_group_wishlists gw ON gw.wishlist_id=sw.id AND gw.group_id=gs.group_id
 JOIN group_members gm ON gm.group_id=gs.group_id AND gm.member_id=h.member_id
 WHERE gs.id=h.wish_id AND gs.wishlist_id=h.wishlist_id AND gs.group_id=h.suggestion_group_id
 AND sw.owner_id<>h.member_id AND NOT sw.is_archived AND NOT sw.is_suspended))`

// ReservationHistory returns durable reservation lifecycles through the member contract.
func (s *Service) ReservationHistory(ctx context.Context, member, status string, page, size int) (domain.PaginatedResponse[domain.GiftReservationHistoryResponse], error) {
	items := []domain.GiftReservationHistoryResponse{}
	var total int
	err := s.Store.Pool.QueryRow(ctx, `SELECT count(*) FROM gift_reservation_histories h WHERE h.member_id=$1 AND ($2='' OR h.status=$2) AND `+suggestionHistoryVisible, member, upperFirst(status)).Scan(&total)
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	if total == 0 || int64(page) > (int64(total)+int64(size)-1)/int64(size) {
		return domain.Page(items, page, size, total), nil
	}
	rows, err := s.Store.Pool.Query(ctx, `SELECT h.id::text,h.wishlist_id::text,CASE WHEN w.owner_id=$1 OR l.id IS NOT NULL OR access.group_id IS NOT NULL THEN COALESCE(w.name,h.wishlist_name) ELSE h.wishlist_name END,h.wish_id::text,CASE WHEN w.owner_id=$1 OR l.id IS NOT NULL OR access.group_id IS NOT NULL THEN COALESCE(g.name,gs.name,h.wish_name) ELSE h.wish_name END,l.id::text,h.quantity,h.status,h.created_at,h.last_activity_at,h.ended_at,
 w.owner_id::text,u.display_name,COALESCE(g.id,gs.id)::text,g.image_id::text,l.protected_secret,COALESCE(w.is_suspended,false),access.group_id::text,h.author_display_name,author.id::text,h.suggestion_group_id::text,COALESCE(w.is_archived,false)
 FROM gift_reservation_histories h
 LEFT JOIN users author ON author.id=h.author_id AND author.email_confirmed
 LEFT JOIN wishlists w ON w.id=h.wishlist_id
 LEFT JOIN users u ON u.id=w.owner_id
 LEFT JOIN wishes g ON g.wishlist_id=w.id AND g.id=h.wish_id
 LEFT JOIN group_suggestions gs ON gs.wishlist_id=w.id AND gs.id=h.wish_id AND gs.group_id=h.suggestion_group_id
 LEFT JOIN wishlist_share_links l ON l.wishlist_id=w.id AND NOT h.is_group_reservation
 LEFT JOIN LATERAL (SELECT gw.group_id FROM visible_group_wishlists gw JOIN group_members gm ON gm.group_id=gw.group_id AND gm.member_id=h.member_id WHERE gw.wishlist_id=w.id AND (h.suggestion_group_id IS NULL OR gw.group_id=h.suggestion_group_id) AND NOT w.is_archived AND NOT w.is_suspended ORDER BY gw.group_id LIMIT 1) access ON true
 WHERE h.member_id=$1 AND ($2='' OR h.status=$2) AND `+suggestionHistoryVisible+`
 ORDER BY h.last_activity_at DESC,h.id DESC LIMIT $3 OFFSET $4`, member, upperFirst(status), size, (int64(page)-1)*int64(size))
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		var r domain.GiftReservationHistoryResponse
		var owner, ownerName, liveWish, image, protected, historicalAuthor, suggestionGroup *string
		var suspended bool
		if err = rows.Scan(&r.Id, &r.WishlistId, &r.WishlistName, &r.WishId, &r.WishName, &r.ShareLinkId, &r.Quantity, &r.Status, &r.CreatedAt, &r.LastActivityAt, &r.EndedAt, &owner, &ownerName, &liveWish, &image, &protected, &suspended, &r.GroupId, &historicalAuthor, &r.OwnerId, &suggestionGroup, &r.IsArchived); err != nil {
			return domain.Page(items, page, size, total), err
		}
		// Lost sharing takes precedence over private wish state: history is not
		// authorization to disclose later edits to another member's private list.
		switch {
		case owner == nil:
			reason := "wishlistDeleted"
			r.InaccessibilityReason = &reason
		case *owner != member && r.ShareLinkId == nil && r.GroupId == nil:
			reason := "notShared"
			r.InaccessibilityReason = &reason
		case r.IsArchived:
			reason := "archived"
			r.InaccessibilityReason = &reason
		case suspended:
			reason := "suspended"
			r.InaccessibilityReason = &reason
		case liveWish == nil:
			reason := "wishDeleted"
			r.InaccessibilityReason = &reason
		}
		r.OwnerDisplayName = historicalAuthor
		// The public author profile remains available independently of wish access.
		// Wish navigation and image grants still require current access.
		if owner != nil && liveWish != nil && !suspended && !r.IsArchived && (*owner == member || r.ShareLinkId != nil || r.GroupId != nil) {
			r.OwnerId, r.OwnerDisplayName = owner, ownerName
			link := ""
			if *owner == member {
				path := "/lists/" + r.WishlistId + "/wishes/" + r.WishId
				r.OwnedWishPath = &path
			} else if suggestionGroup != nil && r.GroupId != nil {
				path := "/groups/" + *r.GroupId + "/wishlists/" + r.WishlistId + "#suggestion-" + r.WishId
				r.GroupSuggestionPath = &path
			} else if r.ShareLinkId == nil && r.GroupId != nil {
				path := "/groups/" + *r.GroupId + "/wishlists/" + r.WishlistId + "/wishes/" + r.WishId
				r.GroupWishPath = &path
			} else {
				link = *r.ShareLinkId
				secret, decryptErr := s.Crypto.Unprotect(*protected, "share|"+link)
				if decryptErr != nil {
					return domain.Page(items, page, size, total), decryptErr
				}
				url := s.Config.FrontendOrigin + "/shared-wishlists/" + link + "#" + secret
				r.ShareUrl = &url
			}
			if image != nil {
				url, imageErr := s.imageURL(*owner, link, r.WishlistId, r.WishId, *image)
				if r.GroupWishPath != nil {
					url, imageErr = s.groupImageURL(member, *r.GroupId, r.WishlistId, r.WishId, *image)
				}
				if imageErr != nil {
					return domain.Page(items, page, size, total), imageErr
				}
				r.ImageUrl = &url
			}
		}
		r.Status = lowerFirst(r.Status)
		items = append(items, r)
	}
	return domain.Page(items, page, size, total), rows.Err()
}

// OwnedReservation preserves the original public service entry point.
func (s *Service) OwnedReservation(ctx context.Context, owner, wishlist, wishID, method string, quantity int, expected *uint32) (ReservationResult, error) {
	return s.OwnerReservation(ctx, owner, wishlist, wishID, method, quantity, expected)
}
