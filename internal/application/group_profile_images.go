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
	"github.com/jackc/pgx/v5"
)

func (s *Service) groupProfileImageURL(member, group, image string) (string, error) {
	data, err := json.Marshal(imageGrant{Scope: "groupProfile", Owner: member, Link: group, Image: image, Expires: s.Now().UTC().Add(5 * time.Minute)})
	if err != nil {
		return "", err
	}
	token, err := s.Crypto.Protect(string(data), "imageGrant")
	return "/api/v1/groups/" + group + "/image?token=" + url.QueryEscape(token), err
}

// GroupProfileImage replaces or removes a group photo using the owner's current group version.
func (s *Service) GroupProfileImage(ctx context.Context, member, group string, source []byte, expected uint32, remove bool) (domain.GroupResponse, uint32, error) {
	var normalized images.Result
	var image *string
	var result domain.GroupResponse
	var version uint32
	var err error
	if !remove {
		normalized, err = images.Process(ctx, source, 512, true)
		if err != nil {
			return result, version, err
		}
		id := security.NewID()
		image = &id
		if err = images.WritePending(ctx, s.Config.ImagePath, id, normalized.Content); err != nil {
			return result, version, err
		}
	}
	err = s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		_, current, err := s.lockGroup(ctx, tx, member, group, true)
		if err != nil {
			return err
		}
		if current != expected {
			return stale(domain.GroupVersionConflict)
		}
		var previous *string
		if err = tx.QueryRow(ctx, `SELECT profile_image_id::text FROM private_groups WHERE id=$1`, group).Scan(&previous); err != nil {
			return err
		}
		if remove && previous == nil {
			return missing(domain.GroupImageNotFound)
		}
		if previous != nil {
			if err = s.queueImageDeletion(ctx, tx, *previous); err != nil {
				return err
			}
		}
		if _, err = tx.Exec(ctx, `UPDATE private_groups SET profile_image_id=$2,profile_image_hash=$3,updated_at=$4 WHERE id=$1`, group, image, normalized.Hash, s.Now().UTC()); err != nil {
			return err
		}
		result, version, err = s.scanGroup(member, tx.QueryRow(ctx, groupSummary+` WHERE g.id=$1`, group))
		return err
	})
	if image != nil {
		if err != nil {
			s.removeUnreferencedImage(ctx, *image)
		} else {
			images.Finalize(s.Config.ImagePath, *image)
		}
	}
	return result, version, err
}

func (s *Service) queueGroupImage(ctx context.Context, tx pgx.Tx, group string) error {
	var image *string
	if err := tx.QueryRow(ctx, `SELECT profile_image_id::text FROM private_groups WHERE id=$1 FOR UPDATE`, group).Scan(&image); err != nil {
		return err
	}
	if image != nil {
		return s.queueImageDeletion(ctx, tx, *image)
	}
	return nil
}

// ReadGroupProfileImage checks the signed grant and live membership before reading private bytes.
func (s *Service) ReadGroupProfileImage(ctx context.Context, group, token string) ([]byte, error) {
	plain, err := s.Crypto.Unprotect(token, "imageGrant")
	if err != nil {
		return nil, missing(domain.GroupImageNotFound)
	}
	var grant imageGrant
	if json.Unmarshal([]byte(plain), &grant) != nil || grant.Scope != "groupProfile" || grant.Link != group || !grant.Expires.After(s.Now()) {
		return nil, missing(domain.GroupImageNotFound)
	}
	var image string
	err = s.Store.Pool.QueryRow(ctx, `SELECT g.profile_image_id::text FROM private_groups g JOIN group_members m ON m.group_id=g.id WHERE g.id=$1 AND m.member_id=$2 AND g.profile_image_id=$3`, group, grant.Owner, grant.Image).Scan(&image)
	if errors.Is(err, pgx.ErrNoRows) {
		return nil, missing(domain.GroupImageNotFound)
	}
	if err != nil {
		return nil, err
	}
	content, err := images.Read(ctx, s.Config.ImagePath, image)
	if errors.Is(err, os.ErrNotExist) {
		err = missing(domain.GroupImageNotFound)
	}
	return content, err
}
