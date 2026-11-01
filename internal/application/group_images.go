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
	"github.com/jackc/pgx/v5"
)

func (s *Service) groupImageURL(member, group, wishlist, wish, image string) (string, error) {
	data, err := json.Marshal(imageGrant{Scope: "group", Owner: member, Link: group, Wishlist: wishlist, Wish: wish, Image: image, Expires: s.Now().UTC().Add(5 * time.Minute)})
	if err != nil {
		return "", err
	}
	token, err := s.Crypto.Protect(string(data), "imageGrant")
	return "/api/v1/groups/" + group + "/wishlists/" + wishlist + "/wishes/" + wish + "/image?token=" + url.QueryEscape(token), err
}

// ReadGroupWishImage rechecks live membership and list sharing for every short-lived image grant.
func (s *Service) ReadGroupWishImage(ctx context.Context, group, wishlist, wish, token string) ([]byte, error) {
	plain, err := s.Crypto.Unprotect(token, "imageGrant")
	if err != nil {
		return nil, missing(domain.WishImageNotFound)
	}
	var grant imageGrant
	if json.Unmarshal([]byte(plain), &grant) != nil || grant.Scope != "group" || grant.Link != group || grant.Wishlist != wishlist || grant.Wish != wish || !grant.Expires.After(s.Now()) {
		return nil, missing(domain.WishImageNotFound)
	}
	var image string
	err = s.Store.Pool.QueryRow(ctx, `SELECT w.image_id::text FROM wishes w JOIN wishlists l ON l.id=w.wishlist_id JOIN visible_group_wishlists gw ON gw.wishlist_id=l.id JOIN group_members m ON m.group_id=gw.group_id WHERE gw.group_id=$1 AND m.member_id=$2 AND w.wishlist_id=$3 AND w.id=$4 AND w.image_id=$5 AND NOT l.is_archived AND NOT l.is_suspended`, group, grant.Owner, wishlist, wish, grant.Image).Scan(&image)
	if errors.Is(err, pgx.ErrNoRows) {
		return nil, missing(domain.WishImageNotFound)
	}
	if err != nil {
		return nil, err
	}
	data, err := images.Read(ctx, s.Config.ImagePath, image)
	if errors.Is(err, os.ErrNotExist) {
		err = missing(domain.WishImageNotFound)
	}
	return data, err
}
