package application

import (
	"context"
	"errors"
	"os"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// GroupJoinPreview reads advertised identity without enrolling or exposing private content.
// A code resolves legacy links; a group identifier resolves the public social preview.
// Activating an invitation advertises its name and photo until the link is revoked.
func (s *Service) GroupJoinPreview(ctx context.Context, group, code string) (domain.GroupJoinPreviewResponse, error) {
	var result domain.GroupJoinPreviewResponse
	query := `SELECT g.id::text,g.name,(SELECT count(*) FROM group_members m WHERE m.group_id=g.id),g.profile_image_id::text FROM private_groups g JOIN group_join_links l ON l.group_id=g.id WHERE `
	var key any = group
	if group == "" {
		query += `l.code_hash=$1`
		key = security.HashToken(code)
	} else {
		query += `g.id=$1`
	}
	var image *string
	err := s.Store.Pool.QueryRow(ctx, query, key).Scan(&result.Id, &result.Name, &result.MemberCount, &image)
	if errors.Is(err, pgx.ErrNoRows) {
		return result, missing(domain.GroupJoinLinkNotFound)
	}
	if err != nil {
		return result, err
	}
	if image != nil {
		path := "/api/v1/groups/" + result.Id + "/preview/image"
		result.ProfileImageUrl = &path
	}
	return result, nil
}

// ReadGroupJoinPreviewImage serves only the current normalized photo of an advertised group.
func (s *Service) ReadGroupJoinPreviewImage(ctx context.Context, group string) ([]byte, error) {
	var image *string
	err := s.Store.Pool.QueryRow(ctx, `SELECT g.profile_image_id::text FROM private_groups g JOIN group_join_links l ON l.group_id=g.id WHERE g.id=$1`, group).Scan(&image)
	if errors.Is(err, pgx.ErrNoRows) {
		return nil, missing(domain.GroupJoinLinkNotFound)
	}
	if err != nil {
		return nil, err
	}
	if image == nil {
		return nil, nil
	}
	content, err := images.Read(ctx, s.Config.ImagePath, *image)
	if errors.Is(err, os.ErrNotExist) {
		return nil, nil
	}
	return content, err
}
