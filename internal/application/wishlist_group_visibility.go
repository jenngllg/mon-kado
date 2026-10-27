package application

import (
	"context"
	"sort"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/jackc/pgx/v5"
)

// WishlistGroupVisibility reads the owner's audience without creating grants.
func (s *Service) WishlistGroupVisibility(ctx context.Context, owner, wishlist string) (domain.WishlistGroupVisibilityResponse, uint32, error) {
	var result domain.WishlistGroupVisibilityResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, false); err != nil {
			return err
		}
		var err error
		result, version, err = s.readWishlistGroupVisibility(ctx, tx, owner, wishlist)
		return err
	})
	return result, version, err
}

func (s *Service) readWishlistGroupVisibility(ctx context.Context, tx pgx.Tx, owner, wishlist string) (domain.WishlistGroupVisibilityResponse, uint32, error) {
	result := domain.WishlistGroupVisibilityResponse{WishlistId: wishlist, Groups: []domain.WishlistGroupVisibilityResponseItem{}}
	var version uint32
	err := tx.QueryRow(ctx, `SELECT v.xmin::text::bigint,EXISTS(SELECT 1 FROM wishlist_share_links l WHERE l.wishlist_id=v.wishlist_id) FROM wishlist_group_visibility v WHERE v.wishlist_id=$1`, wishlist).Scan(&version, &result.IsPublic)
	if err != nil {
		return result, version, err
	}
	rows, err := tx.Query(ctx, `SELECT g.id::text,g.name,EXISTS(SELECT 1 FROM visible_group_wishlists gw WHERE gw.group_id=g.id AND gw.wishlist_id=$2) FROM group_members m JOIN private_groups g ON g.id=m.group_id WHERE m.member_id=$1 ORDER BY lower(g.name),g.id`, owner, wishlist)
	if err != nil {
		return result, version, err
	}
	defer rows.Close()
	for rows.Next() {
		var item domain.WishlistGroupVisibilityResponseItem
		if err = rows.Scan(&item.GroupId, &item.Name, &item.IsVisible); err != nil {
			return result, version, err
		}
		result.Groups = append(result.Groups, item)
	}
	return result, version, rows.Err()
}

func (s *Service) touchWishlistGroupVisibility(ctx context.Context, tx pgx.Tx, wishlist string) error {
	_, err := tx.Exec(ctx, `UPDATE wishlist_group_visibility SET updated_at=$2 WHERE wishlist_id=$1`, wishlist, s.Now().UTC())
	return err
}

// SetWishlistGroupVisibility commits all supplied choices under the audience's current version.
func (s *Service) SetWishlistGroupVisibility(ctx context.Context, owner, wishlist string, input domain.SetWishlistGroupVisibilityRequest, expected uint32) (domain.WishlistGroupVisibilityResponse, uint32, error) {
	var result domain.WishlistGroupVisibilityResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.lockWishlist(ctx, tx, owner, wishlist, true); err != nil {
			return err
		}
		current, currentVersion, err := s.readWishlistGroupVisibility(ctx, tx, owner, wishlist)
		if err != nil {
			return err
		}
		if currentVersion != expected {
			return stale(domain.WishlistGroupVisibilityVersionConflict)
		}
		choices := append([]domain.WishlistGroupVisibilitySelection(nil), (*input.Groups)...)
		for index := range choices {
			normalized := strings.ToLower(*choices[index].GroupId)
			choices[index].GroupId = &normalized
		}
		sort.Slice(choices, func(i, j int) bool { return *choices[i].GroupId < *choices[j].GroupId })
		for _, choice := range choices {
			if _, _, err = s.lockGroup(ctx, tx, owner, *choice.GroupId, false); err != nil {
				return err
			}
			if *choice.IsVisible {
				if _, err = tx.Exec(ctx, `DELETE FROM group_wishlist_exclusions WHERE group_id=$1 AND wishlist_id=$2`, *choice.GroupId, wishlist); err != nil {
					return err
				}
				if !current.IsPublic {
					if _, err = tx.Exec(ctx, `INSERT INTO group_wishlists(group_id,wishlist_id,owner_id,created_at) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING`, *choice.GroupId, wishlist, owner, s.Now().UTC()); err != nil {
						return err
					}
				}
			} else {
				if _, err = tx.Exec(ctx, `DELETE FROM group_wishlists WHERE group_id=$1 AND wishlist_id=$2`, *choice.GroupId, wishlist); err != nil {
					return err
				}
				if _, err = tx.Exec(ctx, `INSERT INTO group_wishlist_exclusions(group_id,wishlist_id,owner_id,created_at) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING`, *choice.GroupId, wishlist, owner, s.Now().UTC()); err != nil {
					return err
				}
			}
		}
		if err = s.touchWishlistGroupVisibility(ctx, tx, wishlist); err != nil {
			return err
		}
		result, version, err = s.readWishlistGroupVisibility(ctx, tx, owner, wishlist)
		return err
	})
	return result, version, err
}
