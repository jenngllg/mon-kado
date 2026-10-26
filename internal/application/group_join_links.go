package application

import (
	"context"
	"crypto/rand"
	"encoding/base32"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// GroupJoinLink reads or creates a group's shared admission code; only its owner may revoke it.
func (s *Service) GroupJoinLink(ctx context.Context, member, group, method string, expected *uint32) (domain.GroupJoinLinkResponse, uint32, error) {
	var result domain.GroupJoinLinkResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if _, _, err := s.lockGroup(ctx, tx, member, group, method == "DELETE"); err != nil {
			return err
		}
		var protected string
		err := tx.QueryRow(ctx, `SELECT protected_code,created_at,xmin::text::bigint FROM group_join_links WHERE group_id=$1`, group).Scan(&protected, &result.CreatedAt, &version)
		if err != nil && !errors.Is(err, pgx.ErrNoRows) {
			return err
		}
		if method == "POST" && err == nil {
			return domain.Failure(409, domain.GroupJoinLinkAlreadyExists, "The group already has an active join link.")
		}
		if method != "POST" && errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.GroupJoinLinkNotFound)
		}
		if expected != nil && *expected != version {
			return stale(domain.GroupJoinLinkVersionConflict)
		}
		if method == "DELETE" {
			_, err = tx.Exec(ctx, `DELETE FROM group_join_links WHERE group_id=$1`, group)
			return err
		}
		result.GroupId = group
		if method == "POST" {
			// 80 random bits remain practical to type while resisting online guessing.
			var bytes [10]byte
			if _, err = rand.Read(bytes[:]); err != nil {
				return err
			}
			result.Code = base32.StdEncoding.WithPadding(base32.NoPadding).EncodeToString(bytes[:])
			protected, err = s.Crypto.Protect(result.Code, "group-join|"+group)
			if err != nil {
				return err
			}
			err = tx.QueryRow(ctx, `INSERT INTO group_join_links(group_id,code_hash,protected_code,created_at) VALUES($1,$2,$3,$4) RETURNING created_at,xmin::text::bigint`, group, security.HashToken(result.Code), protected, s.Now().UTC()).Scan(&result.CreatedAt, &version)
		} else {
			result.Code, err = s.Crypto.Unprotect(protected, "group-join|"+group)
		}
		if err != nil {
			return err
		}
		result.JoinUrl = s.Config.FrontendOrigin + "/groups/join#" + result.Code
		return nil
	})
	return result, version, err
}

// JoinGroup enrolls the authenticated code holder atomically, without an organizer approval.
func (s *Service) JoinGroup(ctx context.Context, member, code string) (domain.GroupResponse, bool, error) {
	var result domain.GroupResponse
	created := false
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var group string
		err := tx.QueryRow(ctx, `SELECT group_id::text FROM group_join_links WHERE code_hash=$1`, security.HashToken(code)).Scan(&group)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.GroupJoinLinkNotFound)
		}
		if err != nil {
			return err
		}
		// Acceptance and account deletion lock the user before the group too.
		var user string
		if err = tx.QueryRow(ctx, `SELECT id::text FROM users WHERE id=$1 FOR NO KEY UPDATE`, member).Scan(&user); err != nil {
			return err
		}
		result, _, err = s.scanGroup(member, tx.QueryRow(ctx, groupSummary+` WHERE g.id=$1 FOR UPDATE OF g`, group))
		if err != nil {
			return err
		}
		var valid, enrolled bool
		if err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM group_join_links WHERE group_id=$1 AND code_hash=$2),EXISTS(SELECT 1 FROM group_members WHERE group_id=$1 AND member_id=$3)`, group, security.HashToken(code), member).Scan(&valid, &enrolled); err != nil {
			return err
		}
		if !valid {
			return missing(domain.GroupJoinLinkNotFound)
		}
		if enrolled {
			return nil
		}
		if err = checkMemberGroupLimit(ctx, tx, member); err != nil {
			return err
		}
		var occupied int
		now := s.Now().UTC()
		if err = tx.QueryRow(ctx, `SELECT (SELECT count(*) FROM group_members WHERE group_id=$1)+(SELECT count(*) FROM group_invitations WHERE group_id=$1 AND member_id<>$2 AND expires_at>$3)`, group, member, now).Scan(&occupied); err != nil {
			return err
		}
		if occupied >= groupMemberLimit {
			return domain.Failure(409, domain.GroupLimitReached, "The group has reached its member and invitation limit.")
		}
		if _, err = tx.Exec(ctx, `INSERT INTO group_members(group_id,member_id,joined_at) VALUES($1,$2,$3)`, group, member, now); err != nil {
			return err
		}
		if _, err = tx.Exec(ctx, `DELETE FROM group_invitations WHERE group_id=$1 AND member_id=$2`, group, member); err != nil {
			return err
		}
		result, _, err = s.scanGroup(member, tx.QueryRow(ctx, groupSummary+` WHERE g.id=$1`, group))
		created = err == nil
		return err
	})
	return result, created, err
}
