package application

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

const groupMemberLimit = 100
const memberGroupLimit = 50
const groupInvitationLifetime = 14 * 24 * time.Hour
const groupSummary = `SELECT g.id::text,g.name,g.owner_id::text,(SELECT count(*) FROM group_members m WHERE m.group_id=g.id),g.created_at,g.updated_at,g.xmin::text::bigint,g.profile_image_id::text FROM private_groups g`
const invitationSummary = `SELECT i.id::text,i.group_id::text,g.name,o.display_name,i.member_id::text,u.display_name,u.display_name_tag,i.created_at,i.expires_at FROM group_invitations i JOIN private_groups g ON g.id=i.group_id JOIN users o ON o.id=g.owner_id JOIN users u ON u.id=i.member_id`

// A single snapshot keeps pagination metadata consistent with cascading account
// or list deletion, which does not acquire the group's application-level lock.
func (s *Service) groupReadTransaction(ctx context.Context, read func(pgx.Tx) error) error {
	tx, err := s.Store.Pool.BeginTx(ctx, pgx.TxOptions{IsoLevel: pgx.RepeatableRead})
	if err != nil {
		return err
	}
	defer tx.Rollback(ctx)
	if err = read(tx); err != nil {
		return err
	}
	return tx.Commit(ctx)
}

func (s *Service) scanGroup(member string, row pgx.Row) (domain.GroupResponse, uint32, error) {
	var result domain.GroupResponse
	var version uint32
	var image *string
	err := row.Scan(&result.Id, &result.Name, &result.OwnerId, &result.MemberCount, &result.CreatedAt, &result.UpdatedAt, &version, &image)
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.GroupNotFound)
	}
	if err == nil && image != nil {
		var imageURL string
		imageURL, err = s.groupProfileImageURL(member, result.Id, *image)
		result.ProfileImageUrl = &imageURL
	}
	return result, version, err
}

// lockGroup centralizes membership checks and serializes grants with revocation.
// UUID knowledge and pending invitations never confer membership.
func (s *Service) lockGroup(ctx context.Context, tx pgx.Tx, member, id string, ownerOnly bool) (domain.GroupResponse, uint32, error) {
	group, version, err := s.scanGroup(member, tx.QueryRow(ctx, groupSummary+` WHERE g.id=$1 AND EXISTS(SELECT 1 FROM group_members m WHERE m.group_id=g.id AND m.member_id=$2) FOR UPDATE OF g`, id, member))
	if err == nil && ownerOnly && group.OwnerId != member {
		err = domain.Failure(403, domain.GroupAccessDenied, "Only the group owner can perform this action.")
	}
	return group, version, err
}

func groupPage[T any](ctx context.Context, tx pgx.Tx, query, order string, args []any, page, size int, scan func(pgx.Row) (T, error)) (domain.PaginatedResponse[T], error) {
	items := []T{}
	var total int
	if err := tx.QueryRow(ctx, `SELECT count(*) FROM (`+query+`) summaries`, args...).Scan(&total); err != nil {
		return domain.Page(items, page, size, 0), err
	}
	query += fmt.Sprintf(" ORDER BY %s LIMIT $%d OFFSET $%d", order, len(args)+1, len(args)+2)
	args = append(args, size, int64(page-1)*int64(size))
	rows, err := tx.Query(ctx, query, args...)
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		item, err := scan(rows)
		if err != nil {
			return domain.Page(items, page, size, total), err
		}
		items = append(items, item)
	}
	return domain.Page(items, page, size, total), rows.Err()
}

// Groups lists only accepted memberships, with a consistent paginated snapshot.
func (s *Service) Groups(ctx context.Context, member string, page, size int) (domain.PaginatedResponse[domain.GroupResponse], error) {
	var result domain.PaginatedResponse[domain.GroupResponse]
	tx, err := s.Store.Pool.BeginTx(ctx, pgx.TxOptions{IsoLevel: pgx.RepeatableRead, AccessMode: pgx.ReadOnly})
	if err != nil {
		return result, err
	}
	defer tx.Rollback(ctx)
	result, err = groupPage(ctx, tx, groupSummary+` WHERE EXISTS(SELECT 1 FROM group_members m WHERE m.group_id=g.id AND m.member_id=$1)`, "g.created_at DESC,g.id", []any{member}, page, size, func(row pgx.Row) (domain.GroupResponse, error) { r, _, err := s.scanGroup(member, row); return r, err })
	if err != nil {
		return result, err
	}
	return result, tx.Commit(ctx)
}

func checkMemberGroupLimit(ctx context.Context, tx pgx.Tx, member string) error {
	var id string
	// Serialize membership counts without blocking invitation foreign-key checks.
	if err := tx.QueryRow(ctx, `SELECT id::text FROM users WHERE id=$1 FOR NO KEY UPDATE`, member).Scan(&id); err != nil {
		return err
	}
	var count int
	if err := tx.QueryRow(ctx, `SELECT count(*) FROM group_members WHERE member_id=$1`, member).Scan(&count); err != nil {
		return err
	}
	if count >= memberGroupLimit {
		return domain.Failure(409, domain.GroupLimitReached, "The member has reached the group limit.")
	}
	return nil
}

// CreateGroup creates the owner membership atomically, without sharing any list.
func (s *Service) CreateGroup(ctx context.Context, member string, input domain.SaveGroupRequest) (domain.GroupResponse, uint32, error) {
	var result domain.GroupResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := checkMemberGroupLimit(ctx, tx, member); err != nil {
			return err
		}
		id, now := security.NewID(), s.Now().UTC()
		if _, err := tx.Exec(ctx, `INSERT INTO private_groups(id,owner_id,name,created_at) VALUES($1,$2,$3,$4)`, id, member, strings.TrimSpace(text(input.Name)), now); err != nil {
			return err
		}
		if _, err := tx.Exec(ctx, `INSERT INTO group_members(group_id,member_id,joined_at) VALUES($1,$2,$3)`, id, member, now); err != nil {
			return err
		}
		var err error
		result, version, err = s.scanGroup(member, tx.QueryRow(ctx, groupSummary+` WHERE g.id=$1`, id))
		return err
	})
	return result, version, err
}

// Group reads, renames or deletes a private group; deletion never deletes wishlists.
func (s *Service) Group(ctx context.Context, member, id, method string, input domain.SaveGroupRequest, expected *uint32) (domain.GroupResponse, uint32, error) {
	var result domain.GroupResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var err error
		result, version, err = s.lockGroup(ctx, tx, member, id, method != "GET")
		if err != nil {
			return err
		}
		if expected != nil && *expected != version {
			return stale(domain.GroupVersionConflict)
		}
		switch method {
		case "PUT":
			if _, err = tx.Exec(ctx, `UPDATE private_groups SET name=$2,updated_at=$3 WHERE id=$1`, id, strings.TrimSpace(text(input.Name)), s.Now().UTC()); err != nil {
				return err
			}
			result, version, err = s.scanGroup(member, tx.QueryRow(ctx, groupSummary+` WHERE g.id=$1`, id))
		case "DELETE":
			if err = s.queueGroupImage(ctx, tx, id); err != nil {
				return err
			}
			_, err = tx.Exec(ctx, `DELETE FROM private_groups WHERE id=$1`, id)
		}
		return err
	})
	return result, version, err
}

// GroupMembers reads accepted public identities, without disclosing pending invitees.
func (s *Service) GroupMembers(ctx context.Context, member, id string, page, size int) (domain.PaginatedResponse[domain.GroupMemberResponse], error) {
	var result domain.PaginatedResponse[domain.GroupMemberResponse]
	err := s.groupReadTransaction(ctx, func(tx pgx.Tx) error {
		if _, _, err := s.lockGroup(ctx, tx, member, id, false); err != nil {
			return err
		}
		var err error
		result, err = groupPage(ctx, tx, `SELECT u.id::text,u.display_name,u.display_name_tag,u.profile_image_id::text,m.joined_at FROM group_members m JOIN users u ON u.id=m.member_id WHERE m.group_id=$1`, "m.joined_at,u.id", []any{id}, page, size, func(row pgx.Row) (domain.GroupMemberResponse, error) {
			var r domain.GroupMemberResponse
			var image *string
			err := row.Scan(&r.Id, &r.DisplayName, &r.DisplayNameTag, &image, &r.JoinedAt)
			if image != nil {
				r.ProfileImageUrl = ptrString("/api/v1/members/" + r.Id + "/profile/image?imageId=" + *image)
			}
			return r, err
		})
		return err
	})
	return result, err
}

// RemoveGroupMember removes group-only grants immediately, preserving independent sharing and reservations.
func (s *Service) RemoveGroupMember(ctx context.Context, member, id, target string) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		g, _, err := s.lockGroup(ctx, tx, member, id, target != member)
		if err != nil {
			return err
		}
		if target == g.OwnerId {
			return domain.Failure(409, domain.GroupOwnerCannotLeave, "The owner must delete the group before leaving.")
		}
		command, err := tx.Exec(ctx, `DELETE FROM group_members WHERE group_id=$1 AND member_id=$2`, id, target)
		if err == nil && command.RowsAffected() == 0 {
			return missing(domain.GroupMemberNotFound)
		}
		return err
	})
}

func scanGroupInvitation(row pgx.Row) (domain.GroupInvitationResponse, error) {
	var r domain.GroupInvitationResponse
	err := row.Scan(&r.Id, &r.GroupId, &r.GroupName, &r.OwnerDisplayName, &r.MemberId, &r.DisplayName, &r.DisplayNameTag, &r.CreatedAt, &r.ExpiresAt)
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.GroupInvitationNotFound)
	}
	return r, err
}

// GroupInvitation reads one pending invitation for its recipient or an accepted group member.
func (s *Service) GroupInvitation(ctx context.Context, member, group, id string) (domain.GroupInvitationResponse, error) {
	if group == "" {
		return scanGroupInvitation(s.Store.Pool.QueryRow(ctx, invitationSummary+` WHERE i.id=$1 AND i.member_id=$2 AND i.expires_at>$3`, id, member, s.Now().UTC()))
	}
	var result domain.GroupInvitationResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, _, err := s.lockGroup(ctx, tx, member, group, false); err != nil {
			return err
		}
		var err error
		result, err = scanGroupInvitation(tx.QueryRow(ctx, invitationSummary+` WHERE i.id=$1 AND i.group_id=$2 AND i.expires_at>$3`, id, group, s.Now().UTC()))
		return err
	})
	return result, err
}

// GroupInvitations reads pending invitations for the recipient or accepted group members.
func (s *Service) GroupInvitations(ctx context.Context, member, group string, page, size int) (domain.PaginatedResponse[domain.GroupInvitationResponse], error) {
	var result domain.PaginatedResponse[domain.GroupInvitationResponse]
	err := s.groupReadTransaction(ctx, func(tx pgx.Tx) error {
		column, value := "i.member_id", member
		if group != "" {
			if _, _, err := s.lockGroup(ctx, tx, member, group, false); err != nil {
				return err
			}
			column, value = "i.group_id", group
		}
		var err error
		result, err = groupPage(ctx, tx, invitationSummary+` WHERE `+column+`=$1 AND i.expires_at>$2`, "i.created_at DESC,i.id", []any{value, s.Now().UTC()}, page, size, scanGroupInvitation)
		return err
	})
	return result, err
}

// InviteGroupMember lets any accepted member create a named invitation, never an automatic enrollment.
func (s *Service) InviteGroupMember(ctx context.Context, member, group string, input domain.InviteGroupMemberRequest) (domain.GroupInvitationResponse, error) {
	var result domain.GroupInvitationResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, _, err := s.lockGroup(ctx, tx, member, group, false); err != nil {
			return err
		}
		target, now := text(input.MemberId), s.Now().UTC()
		var exists bool
		if err := tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM users WHERE id=$1 AND email_confirmed AND is_visible_in_member_search)`, target).Scan(&exists); err != nil {
			return err
		}
		if !exists {
			return missing(domain.GroupMemberNotFound)
		}
		if err := tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM group_members WHERE group_id=$1 AND member_id=$2)`, group, target).Scan(&exists); err != nil {
			return err
		}
		if exists {
			return domain.Failure(409, domain.GroupMemberAlreadyExists, "The member already belongs to the group.")
		}
		if _, err := tx.Exec(ctx, `DELETE FROM group_invitations WHERE group_id=$1 AND expires_at<=$2`, group, now); err != nil {
			return err
		}
		var count int
		if err := tx.QueryRow(ctx, `SELECT (SELECT count(*) FROM group_members WHERE group_id=$1)+(SELECT count(*) FROM group_invitations WHERE group_id=$1)`, group).Scan(&count); err != nil {
			return err
		}
		if count >= groupMemberLimit {
			return domain.Failure(409, domain.GroupLimitReached, "The group has reached its member and invitation limit.")
		}
		id := security.NewID()
		_, err := tx.Exec(ctx, `INSERT INTO group_invitations(id,group_id,member_id,created_at,expires_at)VALUES($1,$2,$3,$4,$5)`, id, group, target, now, now.Add(groupInvitationLifetime))
		if duplicate(err, "group_invitations_group_id_member_id_key") {
			return domain.Failure(409, domain.GroupInvitationAlreadyExists, "An invitation is already pending for this member.")
		}
		if err != nil {
			return err
		}
		result, err = scanGroupInvitation(tx.QueryRow(ctx, invitationSummary+` WHERE i.id=$1`, id))
		return err
	})
	return result, err
}

// AnswerGroupInvitation accepts or declines only the authenticated recipient's current invitation.
func (s *Service) AnswerGroupInvitation(ctx context.Context, member, id string, accept bool) (domain.GroupResponse, error) {
	var result domain.GroupResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var group string
		err := tx.QueryRow(ctx, `SELECT group_id::text FROM group_invitations WHERE id=$1 AND member_id=$2 AND expires_at>$3`, id, member, s.Now().UTC()).Scan(&group)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.GroupInvitationNotFound)
		}
		if err != nil {
			return err
		}
		// Lock the parent before the invitation, as every membership mutation does.
		// Recipient deletion locks the user before cascading invitation rows.
		if accept {
			if err = checkMemberGroupLimit(ctx, tx, member); err != nil {
				return err
			}
		}
		result, _, err = s.scanGroup(member, tx.QueryRow(ctx, groupSummary+` WHERE g.id=$1 FOR UPDATE OF g`, group))
		if err != nil {
			return err
		}
		command, err := tx.Exec(ctx, `DELETE FROM group_invitations WHERE id=$1 AND member_id=$2 AND expires_at>$3`, id, member, s.Now().UTC())
		if err != nil {
			return err
		}
		if command.RowsAffected() == 0 {
			return missing(domain.GroupInvitationNotFound)
		}
		if !accept {
			return nil
		}
		if _, err = tx.Exec(ctx, `INSERT INTO group_members(group_id,member_id,joined_at)VALUES($1,$2,$3)`, group, member, s.Now().UTC()); err != nil {
			return err
		}
		result, _, err = s.scanGroup(member, tx.QueryRow(ctx, groupSummary+` WHERE g.id=$1`, group))
		return err
	})
	return result, err
}

// RevokeGroupInvitation cancels a pending invitation without changing accepted memberships.
func (s *Service) RevokeGroupInvitation(ctx context.Context, member, group, id string) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, _, err := s.lockGroup(ctx, tx, member, group, true); err != nil {
			return err
		}
		command, err := tx.Exec(ctx, `DELETE FROM group_invitations WHERE group_id=$1 AND id=$2 AND expires_at>$3`, group, id, s.Now().UTC())
		if err == nil && command.RowsAffected() == 0 {
			return missing(domain.GroupInvitationNotFound)
		}
		return err
	})
}
