package application

import (
	"context"
	"errors"
	"strings"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

const groupSuggestionLimit = 1000

// AuthorizeGroupSuggestion verifies current group sharing and excludes the beneficiary.
func (s *Service) AuthorizeGroupSuggestion(ctx context.Context, member, group, wishlist string) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		_, err := s.suggestionAccess(ctx, tx, member, group, wishlist)
		return err
	})
}

// suggestionAccess locks the same parent and membership as group reading, then
// excludes the beneficiary before any suggestion or image can be read.
func (s *Service) suggestionAccess(ctx context.Context, tx pgx.Tx, member, group, wishlist string) (verifiedShare, error) {
	share, err := s.verifyGroupWishlist(ctx, tx, member, group, wishlist)
	if err != nil {
		return share, err
	}
	if share.Owner == member {
		return share, missing(domain.GroupSuggestionNotFound)
	}
	return share, nil
}

const suggestionSelect = `SELECT s.id::text,s.name,s.note,s.url,s.price::text,s.quantity,
 COALESCE((SELECT sum(r.quantity) FROM group_suggestion_reservations r WHERE r.suggestion_id=s.id),0),
 COALESCE((SELECT r.quantity FROM group_suggestion_reservations r WHERE r.suggestion_id=s.id AND r.member_id=$3),0),
 s.author_id::text,u.display_name,COALESCE(s.author_id=$3,false),s.image_id IS NOT NULL,s.created_at,s.updated_at,s.xmin::text::bigint
 FROM group_suggestions s LEFT JOIN users u ON u.id=s.author_id WHERE s.group_id=$1 AND s.wishlist_id=$2`

func scanSuggestion(row pgx.Row) (domain.GroupSuggestionResponse, uint32, error) {
	var result domain.GroupSuggestionResponse
	var version uint32
	err := row.Scan(&result.Id, &result.Name, &result.Note, &result.Url, &result.Price, &result.Quantity, &result.ReservedQuantity,
		&result.CurrentParticipantReservedQuantity, &result.AuthorId, &result.AuthorDisplayName, &result.CanEdit, &result.HasImage,
		&result.CreatedAt, &result.UpdatedAt, &version)
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.GroupSuggestionNotFound)
	}
	result.EntityTag = EntityTag(version)
	return result, version, err
}

func findSuggestion(ctx context.Context, tx pgx.Tx, member, group, wishlist, id string) (domain.GroupSuggestionResponse, uint32, error) {
	return scanSuggestion(tx.QueryRow(ctx, suggestionSelect+` AND s.id=$4`, group, wishlist, member, id))
}

// GroupSuggestions lists suggestions only in their original active group scope.
func (s *Service) GroupSuggestions(ctx context.Context, member, group, wishlist string) ([]domain.GroupSuggestionResponse, error) {
	result := []domain.GroupSuggestionResponse{}
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, err := s.suggestionAccess(ctx, tx, member, group, wishlist); err != nil {
			return err
		}
		rows, err := tx.Query(ctx, suggestionSelect+` ORDER BY s.created_at,s.id`, group, wishlist, member)
		if err != nil {
			return err
		}
		defer rows.Close()
		for rows.Next() {
			item, _, err := scanSuggestion(rows)
			if err != nil {
				return err
			}
			result = append(result, item)
		}
		return rows.Err()
	})
	return result, err
}

// GroupSuggestion reads one suggestion under the original group authorization.
func (s *Service) GroupSuggestion(ctx context.Context, member, group, wishlist, id string) (domain.GroupSuggestionResponse, uint32, error) {
	var result domain.GroupSuggestionResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, err := s.suggestionAccess(ctx, tx, member, group, wishlist); err != nil {
			return err
		}
		var err error
		result, version, err = findSuggestion(ctx, tx, member, group, wishlist, id)
		return err
	})
	return result, version, err
}

// CreateGroupSuggestion adds a suggestion without altering the beneficiary's list.
func (s *Service) CreateGroupSuggestion(ctx context.Context, member, group, wishlist string, input domain.SaveGroupSuggestionRequest) (domain.GroupSuggestionResponse, uint32, error) {
	var result domain.GroupSuggestionResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, err := s.suggestionAccess(ctx, tx, member, group, wishlist); err != nil {
			return err
		}
		var count int
		if err := tx.QueryRow(ctx, `SELECT count(*) FROM group_suggestions WHERE group_id=$1 AND wishlist_id=$2`, group, wishlist).Scan(&count); err != nil {
			return err
		}
		if count >= groupSuggestionLimit {
			return domain.Failure(409, domain.GroupSuggestionLimitReached, "The group suggestion limit has been reached.")
		}
		id := security.NewID()
		_, err := tx.Exec(ctx, `INSERT INTO group_suggestions(id,group_id,wishlist_id,author_id,name,note,url,price,quantity,created_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)`, id, group, wishlist, member, strings.TrimSpace(text(input.Name)), optional(input.Note), optional(input.Url), input.Price, quantity(input.Quantity), s.Now().UTC())
		if err != nil {
			return err
		}
		result, version, err = findSuggestion(ctx, tx, member, group, wishlist, id)
		if err != nil {
			return err
		}
		return s.notifyRecipients(ctx, tx, notificationEvent{kind: "suggestionAdded", actor: member, wishlist: wishlist, group: group, subject: id}, `SELECT gm.member_id::text FROM group_members gm JOIN wishlists w ON w.id=$2 WHERE gm.group_id=$1 AND gm.member_id<>w.owner_id ORDER BY gm.member_id`, group, wishlist)
	})
	return result, version, err
}

// lockEditableSuggestion serializes editing with reservations and rejects a
// stale author or a reserved suggestion before changing anything.
func lockEditableSuggestion(ctx context.Context, tx pgx.Tx, member, group, wishlist, id string, expected uint32) error {
	var author *string
	var current uint32
	err := tx.QueryRow(ctx, `SELECT author_id::text,xmin::text::bigint FROM group_suggestions WHERE group_id=$1 AND wishlist_id=$2 AND id=$3 FOR UPDATE`, group, wishlist, id).Scan(&author, &current)
	if errors.Is(err, pgx.ErrNoRows) {
		return missing(domain.GroupSuggestionNotFound)
	}
	if err != nil {
		return err
	}
	if author == nil || *author != member {
		return domain.Failure(403, domain.GroupAccessDenied, "Only the author can change this suggestion.")
	}
	if current != expected {
		return stale(domain.GroupSuggestionVersionConflict)
	}
	var reserved bool
	if err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM group_suggestion_reservations WHERE suggestion_id=$1)`, id).Scan(&reserved); err != nil {
		return err
	}
	if reserved {
		return domain.Failure(409, domain.GroupSuggestionReserved, "The suggestion cannot be changed while it is reserved.")
	}
	return nil
}

// ChangeGroupSuggestion edits or removes an author's unreserved suggestion.
func (s *Service) ChangeGroupSuggestion(ctx context.Context, member, group, wishlist, id, method string, input domain.SaveGroupSuggestionRequest, expected uint32) (domain.GroupSuggestionResponse, uint32, error) {
	var result domain.GroupSuggestionResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, err := s.suggestionAccess(ctx, tx, member, group, wishlist); err != nil {
			return err
		}
		if err := lockEditableSuggestion(ctx, tx, member, group, wishlist, id, expected); err != nil {
			return err
		}
		if method == "DELETE" {
			_, err := tx.Exec(ctx, `DELETE FROM group_suggestions WHERE id=$1 AND group_id=$2 AND wishlist_id=$3`, id, group, wishlist)
			return err
		}
		_, err := tx.Exec(ctx, `UPDATE group_suggestions SET name=$4,note=$5,url=$6,price=$7,quantity=$8,updated_at=$9 WHERE id=$1 AND group_id=$2 AND wishlist_id=$3`, id, group, wishlist, strings.TrimSpace(text(input.Name)), optional(input.Note), optional(input.Url), input.Price, quantity(input.Quantity), s.Now().UTC())
		if err != nil {
			return err
		}
		result, version, err = findSuggestion(ctx, tx, member, group, wishlist, id)
		return err
	})
	return result, version, err
}

// ChangeGroupSuggestionImage stores normalized image bytes atomically with the suggestion.
func (s *Service) ChangeGroupSuggestionImage(ctx context.Context, member, group, wishlist, id string, source []byte, expected uint32, remove bool) (domain.GroupSuggestionResponse, uint32, error) {
	var content []byte
	var imageID *string
	if !remove {
		image, err := images.Process(ctx, source, 1600, false)
		if err != nil {
			return domain.GroupSuggestionResponse{}, 0, err
		}
		content = image.Content
		newID := security.NewID()
		imageID = &newID
	}
	var result domain.GroupSuggestionResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, err := s.suggestionAccess(ctx, tx, member, group, wishlist); err != nil {
			return err
		}
		if err := lockEditableSuggestion(ctx, tx, member, group, wishlist, id, expected); err != nil {
			return err
		}
		if remove {
			var exists bool
			if err := tx.QueryRow(ctx, `SELECT image_id IS NOT NULL FROM group_suggestions WHERE id=$1`, id).Scan(&exists); err != nil {
				return err
			}
			if !exists {
				return missing(domain.WishImageNotFound)
			}
		}
		_, err := tx.Exec(ctx, `UPDATE group_suggestions SET image_id=$2,image_content=$3,updated_at=$4 WHERE id=$1`, id, imageID, content, s.Now().UTC())
		if err != nil {
			return err
		}
		result, version, err = findSuggestion(ctx, tx, member, group, wishlist, id)
		return err
	})
	return result, version, err
}

// GroupSuggestionImage returns image bytes only after checking live membership.
func (s *Service) GroupSuggestionImage(ctx context.Context, member, group, wishlist, id string) ([]byte, error) {
	var content []byte
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, err := s.suggestionAccess(ctx, tx, member, group, wishlist); err != nil {
			return err
		}
		err := tx.QueryRow(ctx, `SELECT image_content FROM group_suggestions WHERE group_id=$1 AND wishlist_id=$2 AND id=$3 AND image_id IS NOT NULL`, group, wishlist, id).Scan(&content)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.WishImageNotFound)
		}
		return err
	})
	return content, err
}
