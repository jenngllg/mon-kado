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
		return s.mutateReservation(ctx, tx, share, wishID, member, participant.Id, method, quantity, expected, &result)
	})
	return result, err
}

func (s *Service) mutateReservation(ctx context.Context, tx pgx.Tx, share verifiedShare, wishID, member, participant, method string, quantity int, expected *uint32, result *ReservationResult) error {
	var desired int
	var wishName string
	err := tx.QueryRow(ctx, `SELECT quantity,name FROM wishes WHERE wishlist_id=$1 AND id=$2 FOR UPDATE`, share.Wishlist, wishID).Scan(&desired, &wishName)
	if errors.Is(err, pgx.ErrNoRows) {
		return missing(domain.SharedWishNotFound)
	}
	if err != nil {
		return err
	}
	r := &result.Reservation
	err = tx.QueryRow(ctx, `SELECT id::text,wish_id::text,quantity,created_at,updated_at,xmin::text::bigint FROM gift_reservations WHERE wishlist_id=$1 AND wish_id=$2 AND wishlist_participant_id=$3 FOR UPDATE`, share.Wishlist, wishID, participant).Scan(&r.Id, &r.WishId, &r.Quantity, &r.CreatedAt, &r.UpdatedAt, &result.Version)
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
	if err = tx.QueryRow(ctx, `SELECT COALESCE(sum(quantity),0) FROM gift_reservations WHERE wish_id=$1 AND wishlist_participant_id<>$2`, wishID, participant).Scan(&reserved); err != nil {
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
		err = tx.QueryRow(ctx, `INSERT INTO gift_reservations(id,wishlist_id,wish_id,wishlist_participant_id,quantity,created_at) VALUES($1,$2,$3,$4,$5,$6) RETURNING wish_id::text,quantity,created_at,updated_at,xmin::text::bigint`, r.Id, share.Wishlist, wishID, participant, quantity, now).Scan(&r.WishId, &r.Quantity, &r.CreatedAt, &r.UpdatedAt, &result.Version)
		result.Created = true
	} else {
		err = tx.QueryRow(ctx, `UPDATE gift_reservations SET quantity=$2,updated_at=$3 WHERE id=$1 RETURNING wish_id::text,quantity,created_at,updated_at,xmin::text::bigint`, r.Id, quantity, now).Scan(&r.WishId, &r.Quantity, &r.CreatedAt, &r.UpdatedAt, &result.Version)
	}
	if err != nil {
		return err
	}
	if member != "" {
		_, err = tx.Exec(ctx, `INSERT INTO gift_reservation_histories(id,member_id,wishlist_id,wishlist_name,wish_id,wish_name,quantity,status,created_at,last_activity_at) VALUES($1,$2,$3,$4,$5,$6,$7,'Active',$8,$9) ON CONFLICT(id) DO UPDATE SET quantity=EXCLUDED.quantity,last_activity_at=EXCLUDED.last_activity_at`, r.Id, member, share.Wishlist, share.Name, wishID, wishName, quantity, r.CreatedAt, now)
	}
	return err
}

// OwnedReservation manages only the owner's reservation, using the same capacity and lifecycle rules.
func (s *Service) OwnedReservation(ctx context.Context, owner, wishlist, wishID, method string, quantity int, expected *uint32) (ReservationResult, error) {
	var result ReservationResult
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		var wish string
		if err := tx.QueryRow(ctx, `SELECT id::text FROM wishes WHERE id=$1 AND wishlist_id=$2 FOR UPDATE`, wishID, wishlist).Scan(&wish); err != nil {
			if errors.Is(err, pgx.ErrNoRows) {
				return missing(domain.WishNotFound)
			}
			return err
		}
		var participant string
		err := tx.QueryRow(ctx, `SELECT id::text FROM wishlist_participants WHERE wishlist_id=$1 AND member_id=$2 FOR UPDATE`, wishlist, owner).Scan(&participant)
		if errors.Is(err, pgx.ErrNoRows) {
			if method != "PUT" {
				return missing(domain.GiftReservationNotFound)
			}
			participant = security.NewID()
			_, err = tx.Exec(ctx, `INSERT INTO wishlist_participants(id,wishlist_id,member_id,guest_display_name,created_at) VALUES($1,$2,$3,'',$4)`, participant, wishlist, owner, s.Now().UTC())
		}
		if err != nil {
			return err
		}
		share := verifiedShare{Wishlist: wishlist, Owner: owner}
		if err = tx.QueryRow(ctx, `SELECT name FROM wishlists WHERE id=$1`, wishlist).Scan(&share.Name); err != nil {
			return err
		}
		return s.mutateReservation(ctx, tx, share, wishID, owner, participant, method, quantity, expected, &result)
	})
	return result, err
}

// ReservationHistory returns durable reservation lifecycles through the member contract.
func (s *Service) ReservationHistory(ctx context.Context, member, status string, page, size int) (domain.PaginatedResponse[domain.GiftReservationHistoryResponse], error) {
	items := []domain.GiftReservationHistoryResponse{}
	var total int
	err := s.Store.Pool.QueryRow(ctx, `SELECT count(*) FROM gift_reservation_histories WHERE member_id=$1 AND ($2='' OR status=$2)`, member, upperFirst(status)).Scan(&total)
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	if total == 0 || int64(page) > (int64(total)+int64(size)-1)/int64(size) {
		return domain.Page(items, page, size, total), nil
	}
	rows, err := s.Store.Pool.Query(ctx, `SELECT h.id::text,h.wishlist_id::text,COALESCE(w.name,h.wishlist_name),h.wish_id::text,COALESCE(g.name,h.wish_name),l.id::text,h.quantity,h.status,h.created_at,h.last_activity_at,h.ended_at,u.id::text,u.display_name,COALESCE(w.is_archived,false),CASE WHEN NOT w.is_archived AND NOT w.is_suspended AND g.id IS NOT NULL THEN w.owner_id=$1 ELSE false END,CASE WHEN NOT w.is_archived AND NOT w.is_suspended AND g.id IS NOT NULL THEN l.protected_secret END,g.image_id::text FROM gift_reservation_histories h LEFT JOIN wishlists w ON w.id=h.wishlist_id LEFT JOIN users u ON u.id=w.owner_id LEFT JOIN wishes g ON g.id=h.wish_id AND g.wishlist_id=h.wishlist_id LEFT JOIN wishlist_share_links l ON l.wishlist_id=h.wishlist_id WHERE h.member_id=$1 AND ($2='' OR h.status=$2) ORDER BY h.last_activity_at DESC,h.id DESC LIMIT $3 OFFSET $4`, member, upperFirst(status), size, (int64(page)-1)*int64(size))
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		var r domain.GiftReservationHistoryResponse
		var owned bool
		var protected, image *string
		if err = rows.Scan(&r.Id, &r.WishlistId, &r.WishlistName, &r.WishId, &r.WishName, &r.ShareLinkId, &r.Quantity, &r.Status, &r.CreatedAt, &r.LastActivityAt, &r.EndedAt, &r.OwnerId, &r.OwnerDisplayName, &r.IsArchived, &owned, &protected, &image); err != nil {
			return domain.Page(items, page, size, total), err
		}
		if owned {
			r.OwnedWishPath = ptrString("/lists/" + r.WishlistId + "/wishes/" + r.WishId)
			if image != nil {
				address, e := s.imageURL(member, "", r.WishlistId, r.WishId, *image)
				if e != nil {
					return domain.Page(items, page, size, total), e
				}
				r.ImageUrl = &address
			}
		} else if r.OwnerId != nil && *r.OwnerId != member && r.ShareLinkId != nil && protected != nil {
			secret, e := s.Crypto.Unprotect(*protected, "share|"+*r.ShareLinkId)
			if e != nil {
				return domain.Page(items, page, size, total), e
			}
			r.ShareUrl = ptrString(s.Config.FrontendOrigin + "/shared-wishlists/" + *r.ShareLinkId + "#" + secret)
			if image != nil {
				address, e := s.imageURL("", *r.ShareLinkId, r.WishlistId, r.WishId, *image)
				if e != nil {
					return domain.Page(items, page, size, total), e
				}
				r.ImageUrl = &address
			}
		}
		r.Status = lowerFirst(r.Status)
		items = append(items, r)
	}
	return domain.Page(items, page, size, total), rows.Err()
}
