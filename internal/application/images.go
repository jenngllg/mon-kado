package application

import (
	"context"
	"encoding/json"
	"errors"
	"net/url"
	"os"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
)

type imageGrant struct {
	Scope    string    `json:"scope"`
	Owner    string    `json:"owner"`
	Link     string    `json:"link"`
	Wishlist string    `json:"wishlist"`
	Wish     string    `json:"wish"`
	Image    string    `json:"image"`
	Expires  time.Time `json:"expires"`
}

func (s *Service) imageURL(owner, link, wishlist, wish, image string) (string, error) {
	scope, path := "owned", "/api/v1/wishlists/"+wishlist+"/wishes/"+wish+"/image"
	if link != "" {
		scope, path = "shared", "/api/v1/shared-wishlists/"+link+"/wishes/"+wish+"/image"
	}
	data, err := json.Marshal(imageGrant{Scope: scope, Owner: owner, Link: link, Wishlist: wishlist, Wish: wish, Image: image, Expires: s.Now().UTC().Add(5 * time.Minute)})
	if err != nil {
		return "", err
	}
	token, err := s.Crypto.Protect(string(data), "imageGrant")
	return path + "?token=" + url.QueryEscape(token), err
}

// UpsertWishImage stages normalized bytes and atomically replaces the referenced image.
func (s *Service) UpsertWishImage(ctx context.Context, owner, wishlist, wish string, source []byte, expected uint32) (domain.WishResponse, uint32, error) {
	result, err := images.Process(ctx, source, 1600, false)
	if err != nil {
		return domain.WishResponse{}, 0, err
	}
	id := security.NewID()
	if err = images.WritePending(ctx, s.Config.ImagePath, id, result.Content); err != nil {
		return domain.WishResponse{}, 0, err
	}
	var response domain.WishResponse
	var version uint32
	err = s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		var current uint32
		var previous *string
		err := tx.QueryRow(ctx, `SELECT xmin::text::bigint,image_id::text FROM wishes WHERE wishlist_id=$1 AND id=$2 FOR UPDATE`, wishlist, wish).Scan(&current, &previous)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.WishNotFound)
		}
		if err != nil {
			return err
		}
		if current != expected {
			return stale(domain.WishVersionConflict)
		}
		if previous != nil {
			if err = s.queueImageDeletion(ctx, tx, *previous); err != nil {
				return err
			}
		}
		response, version, err = s.scanWish(owner, tx.QueryRow(ctx, `UPDATE wishes SET image_id=$3,image_content_hash=$4,updated_at=$5 WHERE wishlist_id=$1 AND id=$2 RETURNING `+wishColumns, wishlist, wish, id, result.Hash, s.Now().UTC()))
		return err
	})
	if err != nil {
		s.removeUnreferencedImage(ctx, id)
		return response, version, err
	}
	images.Finalize(s.Config.ImagePath, id)
	return response, version, nil
}

// DeleteWishImage clears the reference and queues physical removal in one transaction.
func (s *Service) DeleteWishImage(ctx context.Context, owner, wishlist, wish string, expected uint32) (uint32, error) {
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		var current uint32
		var image *string
		err := tx.QueryRow(ctx, `SELECT xmin::text::bigint,image_id::text FROM wishes WHERE wishlist_id=$1 AND id=$2 FOR UPDATE`, wishlist, wish).Scan(&current, &image)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.WishNotFound)
		}
		if err != nil {
			return err
		}
		if current != expected {
			return stale(domain.WishVersionConflict)
		}
		if image == nil {
			return missing(domain.WishImageNotFound)
		}
		if err = s.queueImageDeletion(ctx, tx, *image); err != nil {
			return err
		}
		return tx.QueryRow(ctx, `UPDATE wishes SET image_id=NULL,image_content_hash=NULL,updated_at=$3 WHERE wishlist_id=$1 AND id=$2 RETURNING xmin::text::bigint`, wishlist, wish, s.Now().UTC()).Scan(&version)
	})
	return version, err
}

// ReadWishImage validates a short-lived resource-bound grant before accessing storage.
func (s *Service) ReadWishImage(ctx context.Context, wishlist, link, wish, token string) ([]byte, []byte, error) {
	plain, err := s.Crypto.Unprotect(token, "imageGrant")
	if err != nil {
		return nil, nil, missing(domain.WishImageNotFound)
	}
	var grant imageGrant
	if err = json.Unmarshal([]byte(plain), &grant); err != nil || !grant.Expires.After(s.Now()) || grant.Wish != wish || (link == "" && (grant.Scope != "owned" || grant.Wishlist != wishlist || grant.Owner == "")) || (link != "" && (grant.Scope != "shared" || grant.Link != link)) {
		return nil, nil, missing(domain.WishImageNotFound)
	}
	var valid bool
	var hash []byte
	if link == "" {
		err = s.Store.Pool.QueryRow(ctx, `SELECT true,w.image_content_hash FROM wishes w JOIN wishlists l ON l.id=w.wishlist_id WHERE w.id=$1 AND w.wishlist_id=$2 AND w.image_id=$3 AND l.owner_id=$4`, wish, grant.Wishlist, grant.Image, grant.Owner).Scan(&valid, &hash)
	} else {
		err = s.Store.Pool.QueryRow(ctx, `SELECT true,w.image_content_hash FROM wishes w JOIN wishlists l ON l.id=w.wishlist_id JOIN wishlist_share_links sl ON sl.wishlist_id=l.id WHERE w.id=$1 AND w.wishlist_id=$2 AND w.image_id=$3 AND sl.id=$4 AND NOT l.is_suspended AND NOT l.is_archived`, wish, grant.Wishlist, grant.Image, link).Scan(&valid, &hash)
	}
	if errors.Is(err, pgx.ErrNoRows) {
		return nil, nil, missing(domain.WishImageNotFound)
	}
	if err != nil {
		return nil, nil, err
	}
	content, err := images.Read(ctx, s.Config.ImagePath, grant.Image)
	if errors.Is(err, os.ErrNotExist) {
		err = missing(domain.WishImageNotFound)
	}
	return content, hash, err
}

// ProfileImage changes or removes the current member's public photo using its profile ETag.
func (s *Service) ProfileImage(ctx context.Context, user string, source []byte, expected uint32, remove bool) (domain.MemberProfileResponse, uint32, error) {
	var normalized images.Result
	var err error
	var id *string
	if !remove {
		normalized, err = images.Process(ctx, source, 512, true)
		if err != nil {
			return domain.MemberProfileResponse{}, 0, err
		}
		newID := security.NewID()
		id = &newID
		if err = images.WritePending(ctx, s.Config.ImagePath, newID, normalized.Content); err != nil {
			return domain.MemberProfileResponse{}, 0, err
		}
	}
	var result domain.MemberProfileResponse
	var version uint32
	err = s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var previous *string
		var current uint32
		if err := tx.QueryRow(ctx, `SELECT profile_image_id::text,xmin::text::bigint FROM users WHERE id=$1 FOR UPDATE`, user).Scan(&previous, &current); err != nil {
			return err
		}
		if current != expected {
			return stale(domain.MemberProfileVersionConflict)
		}
		if remove && previous == nil {
			return missing(domain.ProfileImageNotFound)
		}
		if previous != nil {
			if err := s.queueImageDeletion(ctx, tx, *previous); err != nil {
				return err
			}
		}
		return tx.QueryRow(ctx, `UPDATE users SET profile_image_id=$2,profile_image_hash=$3,updated_at=$4 WHERE id=$1 RETURNING display_name,display_name_tag,xmin::text::bigint,is_visible_in_member_search`, user, id, normalized.Hash, s.Now().UTC()).Scan(&result.DisplayName, &result.DisplayNameTag, &version, &result.IsVisibleInMemberSearch)
	})
	if id != nil {
		if err != nil {
			s.removeUnreferencedImage(ctx, *id)
		} else {
			images.Finalize(s.Config.ImagePath, *id)
			result.ProfileImageUrl = ptrString("/api/v1/members/" + user + "/profile/image?imageId=" + *id)
		}
	}
	return result, version, err
}

func (s *Service) removeUnreferencedImage(ctx context.Context, id string) {
	var referenced bool
	// Keep staged bytes when the database cannot resolve an ambiguous commit.
	if err := s.Store.Pool.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM wishes WHERE image_id=$1 UNION ALL SELECT 1 FROM users WHERE profile_image_id=$1 UNION ALL SELECT 1 FROM private_groups WHERE profile_image_id=$1)`, id).Scan(&referenced); err == nil && !referenced {
		images.Remove(s.Config.ImagePath, id)
	}
}

// ReadProfileImage reads the public normalized profile image of a confirmed member.
func (s *Service) ReadProfileImage(ctx context.Context, user, imageID string) ([]byte, []byte, error) {
	parsed, err := uuid.Parse(imageID)
	if err != nil || parsed == uuid.Nil {
		return nil, nil, missing(domain.ProfileImageNotFound)
	}
	imageID = parsed.String()
	var id string
	var hash []byte
	err = s.Store.Pool.QueryRow(ctx, `SELECT profile_image_id::text,profile_image_hash FROM users WHERE id=$1 AND email_confirmed AND profile_image_id=$2`, user, imageID).Scan(&id, &hash)
	if errors.Is(err, pgx.ErrNoRows) {
		return nil, nil, missing(domain.ProfileImageNotFound)
	}
	if err != nil {
		return nil, nil, err
	}
	content, err := images.Read(ctx, s.Config.ImagePath, id)
	if errors.Is(err, os.ErrNotExist) {
		err = missing(domain.ProfileImageNotFound)
	}
	return content, hash, err
}
