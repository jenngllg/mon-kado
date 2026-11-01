package application

import (
	"context"
	"errors"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

const wishlistParticipantLimit = 100

// ShareLink creates, reads, rotates or revokes a wishlist's active sharing secret.
func (s *Service) ShareLink(ctx context.Context, owner, wishlist, method string, expected *uint32) (domain.WishlistShareLinkResponse, uint32, error) {
	var result domain.WishlistShareLinkResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, method != "GET"); err != nil {
			return err
		}
		var protected string
		err := tx.QueryRow(ctx, `SELECT id::text,protected_secret,created_at,updated_at,xmin::text::bigint FROM wishlist_share_links WHERE wishlist_id=$1 FOR UPDATE`, wishlist).Scan(&result.Id, &protected, &result.CreatedAt, &result.UpdatedAt, &version)
		if err != nil && !errors.Is(err, pgx.ErrNoRows) {
			return err
		}
		if method == "POST" && err == nil {
			return domain.Failure(409, domain.WishlistShareLinkAlreadyExists, "The wishlist already has an active share link.")
		}
		if method != "POST" && errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.WishlistShareLinkNotFound)
		}
		if expected != nil && *expected != version {
			return stale(domain.WishlistShareLinkVersionConflict)
		}
		if method == "DELETE" {
			_, err = tx.Exec(ctx, `DELETE FROM wishlist_share_links WHERE wishlist_id=$1`, wishlist)
			if err == nil {
				err = s.touchWishlistGroupVisibility(ctx, tx, wishlist)
			}
			return err
		}
		if method == "POST" {
			result.Id = security.NewID()
		}
		var secret string
		if method == "GET" {
			secret, err = s.Crypto.Unprotect(protected, "share|"+result.Id)
			if err != nil {
				return err
			}
		}
		if method == "POST" || method == "PUT" {
			secret = security.RandomToken()
			protected, err = s.Crypto.Protect(secret, "share|"+result.Id)
			if err != nil {
				return err
			}
			now := s.Now().UTC()
			if method == "POST" {
				err = tx.QueryRow(ctx, `INSERT INTO wishlist_share_links(id,wishlist_id,secret_hash,protected_secret,created_at) VALUES($1,$2,$3,$4,$5) RETURNING created_at,updated_at,xmin::text::bigint`, result.Id, wishlist, security.HashToken(secret), protected, now).Scan(&result.CreatedAt, &result.UpdatedAt, &version)
			}
			if method == "PUT" {
				err = tx.QueryRow(ctx, `UPDATE wishlist_share_links SET secret_hash=$2,protected_secret=$3,updated_at=$4 WHERE id=$1 RETURNING created_at,updated_at,xmin::text::bigint`, result.Id, security.HashToken(secret), protected, now).Scan(&result.CreatedAt, &result.UpdatedAt, &version)
			}
			if err != nil {
				return err
			}
		}
		if method == "POST" {
			if err = s.touchWishlistGroupVisibility(ctx, tx, wishlist); err != nil {
				return err
			}
		}
		result.ShareUrl = s.Config.FrontendOrigin + "/shared-wishlists/" + result.Id + "#" + secret
		return nil
	})
	return result, version, err
}

type verifiedShare struct {
	Group        string
	Member       string
	Wishlist     string
	Owner        string
	Name         string
	SurpriseMode bool
}

func (s *Service) verifyShare(ctx context.Context, tx pgx.Tx, id, secret string) (verifiedShare, error) {
	var share verifiedShare
	var hash []byte
	var suspended, archived bool
	err := tx.QueryRow(ctx, `SELECT l.wishlist_id::text,l.secret_hash,w.owner_id::text,w.name,w.is_suspended,w.is_archived,w.surprise_mode FROM wishlist_share_links l JOIN wishlists w ON w.id=l.wishlist_id WHERE l.id=$1 FOR UPDATE OF w,l`, id).Scan(&share.Wishlist, &hash, &share.Owner, &share.Name, &suspended, &archived, &share.SurpriseMode)
	if errors.Is(err, pgx.ErrNoRows) {
		return share, missing(domain.SharedWishlistNotFound)
	}
	if err != nil {
		return share, err
	}
	if suspended || archived || !security.VerifyToken(secret, hash) {
		return share, missing(domain.SharedWishlistNotFound)
	}
	return share, nil
}

func (s *Service) guestIdentity(ctx context.Context, tx pgx.Tx, token string) (string, error) {
	id, err := security.SessionID(token)
	if err != nil {
		return "", domain.Failure(401, domain.GuestSessionInvalid, "The guest session is invalid.")
	}
	var hash []byte
	var expires time.Time
	err = tx.QueryRow(ctx, `SELECT secret_hash,expires_at FROM guest_sessions WHERE id=$1`, id).Scan(&hash, &expires)
	if errors.Is(err, pgx.ErrNoRows) {
		return "", domain.Failure(401, domain.GuestSessionInvalid, "The guest session is invalid.")
	}
	if err != nil {
		return "", err
	}
	if !expires.After(s.Now()) || !security.VerifyToken(token, hash) {
		return "", domain.Failure(401, domain.GuestSessionInvalid, "The guest session is invalid.")
	}
	return id, nil
}

func (s *Service) participant(ctx context.Context, tx pgx.Tx, share verifiedShare, member, guest string) (*domain.WishlistParticipantResponse, error) {
	var result domain.WishlistParticipantResponse
	var guestID *string
	if member == "" && guest != "" {
		id, err := s.guestIdentity(ctx, tx, guest)
		if err != nil {
			return nil, err
		}
		guestID = &id
	}
	if member == "" && guestID == nil {
		return nil, nil
	}
	var memberID *string
	if member != "" {
		memberID = &member
	}
	err := tx.QueryRow(ctx, `SELECT p.id::text,COALESCE(u.display_name,p.guest_display_name) FROM wishlist_participants p LEFT JOIN users u ON u.id=p.member_id WHERE p.wishlist_id=$1 AND (p.member_id=$2 OR p.guest_session_id=$3)`, share.Wishlist, memberID, guestID).Scan(&result.Id, &result.DisplayName)
	if errors.Is(err, pgx.ErrNoRows) {
		return nil, nil
	}
	return &result, err
}

// JoinResult distinguishes a new participant and carries only newly generated guest material.
type JoinResult struct {
	Participant  domain.WishlistParticipantResponse
	Created      bool
	GuestToken   string
	GuestExpires time.Time
}

// JoinWishlist atomically enrolls an identified member or a persistent guest.
func (s *Service) JoinWishlist(ctx context.Context, link, secret, member, guest string, input domain.JoinSharedWishlistRequest) (JoinResult, error) {
	var result JoinResult
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyShare(ctx, tx, link, secret)
		if err != nil {
			return err
		}
		if share.Owner == member {
			return domain.Failure(409, domain.WishlistOwnerCannotJoin, "The owner cannot participate in their own wishlist.")
		}
		existing, err := s.participant(ctx, tx, share, member, guest)
		if err != nil {
			return err
		}
		if existing != nil {
			result.Participant = *existing
			return nil
		}
		var count int
		if err = tx.QueryRow(ctx, `SELECT count(*) FROM wishlist_participants WHERE wishlist_id=$1 AND (member_id IS NOT NULL OR guest_session_id IS NOT NULL)`, share.Wishlist).Scan(&count); err != nil {
			return err
		}
		if count >= wishlistParticipantLimit {
			return domain.Failure(409, domain.WishlistParticipantLimitReached, "The wishlist has reached its participant limit.")
		}
		var memberID, guestID *string
		name := strings.TrimSpace(text(input.DisplayName))
		now := s.Now().UTC()
		if member != "" {
			memberID = &member
			if err = tx.QueryRow(ctx, `SELECT display_name FROM users WHERE id=$1`, member).Scan(&name); err != nil {
				return err
			}
		} else {
			id := ""
			if guest != "" {
				id, err = s.guestIdentity(ctx, tx, guest)
				if err != nil {
					return err
				}
			}
			if id == "" {
				id = security.NewID()
				result.GuestToken = security.SessionToken(id)
				result.GuestExpires = now.Add(180 * 24 * time.Hour)
				if _, err = tx.Exec(ctx, `INSERT INTO guest_sessions(id,secret_hash,expires_at,created_at) VALUES($1,$2,$3,$4)`, id, security.HashToken(result.GuestToken), result.GuestExpires, now); err != nil {
					return err
				}
			}
			guestID = &id
		}
		result.Participant = domain.WishlistParticipantResponse{Id: security.NewID(), DisplayName: name}
		_, err = tx.Exec(ctx, `INSERT INTO wishlist_participants(id,wishlist_id,member_id,guest_session_id,guest_display_name,created_at) VALUES($1,$2,$3,$4,$5,$6)`, result.Participant.Id, share.Wishlist, memberID, guestID, name, now)
		result.Created = true
		return err
	})
	return result, err
}

// CurrentParticipant returns the participant proved by the member bearer or guest cookie.
func (s *Service) CurrentParticipant(ctx context.Context, link, secret, member, guest string) (domain.WishlistParticipantResponse, error) {
	var result domain.WishlistParticipantResponse
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
		result = *participant
		return nil
	})
	return result, err
}

func (s *Service) sharedWish(ctx context.Context, tx pgx.Tx, share verifiedShare, link, id string, participant *domain.WishlistParticipantResponse) (domain.SharedWishDetailResponse, error) {
	r := domain.SharedWishDetailResponse{CanReserve: share.Owner != share.Member}
	var image *string
	var participantID *string
	if participant != nil && r.CanReserve {
		participantID = &participant.Id
	}
	err := tx.QueryRow(ctx, `SELECT w.id::text,w.name,w.note,w.url,w.price::text,w.quantity,COALESCE((SELECT sum(r.quantity) FROM gift_reservations r WHERE r.wish_id=w.id),0),CASE WHEN $3::uuid IS NULL THEN NULL ELSE COALESCE((SELECT r.quantity FROM gift_reservations r WHERE r.wish_id=w.id AND r.wishlist_participant_id=$3),0) END,w.image_id::text,w.is_favorite,w.color,w.size,w.is_surprise,w.accepts_second_hand FROM wishes w WHERE w.wishlist_id=$1 AND w.id=$2`, share.Wishlist, id, participantID).Scan(&r.Id, &r.Name, &r.Note, &r.Url, &r.Price, &r.Quantity, &r.ReservedQuantity, &r.CurrentParticipantReservedQuantity, &image, &r.IsFavorite, &r.Color, &r.Size, &r.IsSurprise, &r.AcceptsSecondHand)
	if errors.Is(err, pgx.ErrNoRows) {
		return r, missing(domain.SharedWishNotFound)
	}
	if err != nil {
		return r, err
	}
	available := max(0, r.Quantity-*r.ReservedQuantity)
	r.AvailableQuantity = &available
	if !r.CanReserve && share.SurpriseMode {
		// Null every reservation-derived value before any response or availability filter.
		r.ReservedQuantity, r.AvailableQuantity, r.CurrentParticipantReservedQuantity = nil, nil, nil
	}
	if image != nil {
		imageURL, urlErr := s.imageURL("", link, share.Wishlist, id, *image)
		if share.Group != "" {
			imageURL, urlErr = s.groupImageURL(share.Member, share.Group, share.Wishlist, id, *image)
		}
		if urlErr != nil {
			return r, urlErr
		}
		r.ImageUrl = &imageURL
	}
	return r, err
}

// SharedWish retrieves public gift details without disclosing participant identities.
func (s *Service) SharedWish(ctx context.Context, link, secret, id, member, guest string) (domain.SharedWishDetailResponse, error) {
	var r domain.SharedWishDetailResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyShare(ctx, tx, link, secret)
		if err != nil {
			return err
		}
		share.Member = member
		participant, err := s.participant(ctx, tx, share, member, guest)
		if err != nil {
			return err
		}
		r, err = s.sharedWish(ctx, tx, share, link, id, participant)
		return err
	})
	return r, err
}

// SharedWishlist returns ordered public content and optionally hides fully reserved gifts.
func (s *Service) SharedWishlist(ctx context.Context, link, secret, member, guest string, availableOnly bool) (domain.SharedWishlistResponse, error) {
	r := domain.SharedWishlistResponse{Wishes: []domain.SharedWishResponse{}}
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyShare(ctx, tx, link, secret)
		if err != nil {
			return err
		}
		share.Member = member
		r.CanReserve = share.Owner != member
		r.CanSubscribe = r.CanReserve
		if err = tx.QueryRow(ctx, `SELECT w.id::text,w.owner_id::text,u.display_name,w.name,w.occasion,w.event_date::text,w.message FROM wishlists w JOIN users u ON u.id=w.owner_id WHERE w.id=$1`, share.Wishlist).Scan(&r.Id, &r.OwnerId, &r.OwnerDisplayName, &r.Name, &r.Occasion, &r.EventDate, &r.Message); err != nil {
			return err
		}
		r.Occasion = lowerFirst(r.Occasion)
		if r.CanReserve {
			r.CurrentParticipant, err = s.participant(ctx, tx, share, member, guest)
			if err != nil {
				return err
			}
		}
		rows, err := tx.Query(ctx, `SELECT id::text FROM wishes WHERE wishlist_id=$1 ORDER BY position`, share.Wishlist)
		if err != nil {
			return err
		}
		ids, err := pgx.CollectRows(rows, pgx.RowTo[string])
		if err != nil {
			return err
		}
		for _, id := range ids {
			wish, err := s.sharedWish(ctx, tx, share, link, id, r.CurrentParticipant)
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

// ReportWishlist persists an anonymous report without exposing its moderation state.
func (s *Service) ReportWishlist(ctx context.Context, link, secret string, input domain.ReportSharedWishlistRequest) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.verifyShare(ctx, tx, link, secret)
		if err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `INSERT INTO wishlist_reports(id,wishlist_id,reason,details,created_at) VALUES($1,$2,$3,$4,$5)`, security.NewID(), share.Wishlist, upperFirst(text(input.Reason)), optional(input.Details), s.Now().UTC())
		return err
	})
}
