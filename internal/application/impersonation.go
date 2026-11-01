package application

import (
	"context"
	"errors"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// ImpersonationResponse identifies a delegation without an independent deadline bound to the administrator session.
type ImpersonationResponse struct {
	ID        string                        `json:"id"`
	ExpiresAt *time.Time                    `json:"expiresAt"`
	User      domain.CurrentSessionResponse `json:"user"`
}

// StartImpersonation opens a simulation with real business writes and records its real actor atomically.
func (s *Service) StartImpersonation(ctx context.Context, actor Principal, member string) (ImpersonationResponse, error) {
	var result ImpersonationResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, actor); err != nil {
			return err
		}
		var allowed bool
		if err := tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM users u WHERE u.id=$1 AND u.email_confirmed AND u.id<>$2 AND NOT u.is_admin)`, member, actor.MemberID).Scan(&allowed); err != nil {
			return err
		}
		if !allowed {
			return domain.Failure(403, domain.AccountImpersonationDenied, "This account cannot be impersonated.")
		}
		result.ID = security.NewID()
		now := s.Now().UTC()
		if _, err := tx.Exec(ctx, `INSERT INTO administrator_impersonations(id,administrator_id,member_id,session_id,created_at) VALUES($1,$2,$3,$4,$5)`, result.ID, actor.MemberID, member, actor.SessionID, now); err != nil {
			return err
		}
		var err error
		result.User.IsAdmin = false
		err = tx.QueryRow(ctx, `SELECT id::text,email,display_name,display_name_tag,is_visible_in_member_search FROM users WHERE id=$1`, member).Scan(&result.User.Id, &result.User.Email, &result.User.DisplayName, &result.User.DisplayNameTag, &result.User.IsVisibleInMemberSearch)
		if err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `INSERT INTO member_activity_events(id,member_id,actor_member_id,is_impersonated,occurred_at,kind,activity,log_level) VALUES($1,$2,$3,true,$4,'action','Début de simulation','info')`, security.NewID(), member, actor.MemberID, now)
		return err
	})
	return result, err
}

// ResolveImpersonation checks the live administrator boolean, MFA, target and original session on every request.
func (s *Service) ResolveImpersonation(ctx context.Context, actor Principal, id string) (Principal, error) {
	if actor.MemberID == "" {
		return Principal{}, unauthorized()
	}
	var member string
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, actor); err != nil {
			return err
		}
		return tx.QueryRow(ctx, `SELECT i.member_id::text FROM administrator_impersonations i JOIN users u ON u.id=i.member_id WHERE i.id=$1 AND i.administrator_id=$2 AND i.session_id=$3 AND i.ended_at IS NULL AND u.email_confirmed`, id, actor.MemberID, actor.SessionID).Scan(&member)
	})
	if errors.Is(err, pgx.ErrNoRows) {
		err = domain.Failure(403, domain.AccountImpersonationExpired, "The impersonation is no longer available.")
	}
	return Principal{MemberID: member, ActorMemberID: actor.MemberID, SessionID: actor.SessionID, IsAdmin: false}, err
}

// StopImpersonation ends the delegation and attributes the stop to its administrator.
func (s *Service) StopImpersonation(ctx context.Context, actor Principal, id string) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, actor); err != nil {
			return err
		}
		var member string
		err := tx.QueryRow(ctx, `UPDATE administrator_impersonations SET ended_at=$4 WHERE id=$1 AND administrator_id=$2 AND session_id=$3 AND ended_at IS NULL RETURNING member_id::text`, id, actor.MemberID, actor.SessionID, s.Now().UTC()).Scan(&member)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.RequestNotFound)
		}
		if err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `INSERT INTO member_activity_events(id,member_id,actor_member_id,is_impersonated,occurred_at,kind,activity,log_level) VALUES($1,$2,$3,true,$4,'action','Fin de simulation','info')`, security.NewID(), member, actor.MemberID, s.Now().UTC())
		return err
	})
}
