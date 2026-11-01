package application

import (
	"context"
	"encoding/json"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// ActivityEvent describes a member's visit or action with sanitized request data.
type ActivityEvent struct {
	ID               string          `json:"id"`
	MemberID         string          `json:"memberId"`
	DisplayName      string          `json:"displayName"`
	DisplayNameTag   string          `json:"displayNameTag"`
	OccurredAt       time.Time       `json:"occurredAt"`
	Kind             string          `json:"kind"`
	Activity         string          `json:"activity"`
	LogLevel         string          `json:"logLevel"`
	Payload          json.RawMessage `json:"payload"`
	ActorMemberID    *string         `json:"actorMemberId"`
	ActorDisplayName *string         `json:"actorDisplayName"`
	IsImpersonated   bool            `json:"isImpersonated"`
}

// RecordActivity stores a bounded, caller-provided activity label for an authenticated member.
func (s *Service) RecordActivity(ctx context.Context, member, kind, activity, level string, payload json.RawMessage, actor ...string) error {
	actual := member
	if len(actor) > 0 && actor[0] != "" {
		actual = actor[0]
	}
	_, err := s.Store.Pool.Exec(ctx, `INSERT INTO member_activity_events(id,member_id,occurred_at,kind,activity,log_level,payload,actor_member_id,is_impersonated) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9)`, security.NewID(), member, s.Now().UTC(), kind, activity, level, payload, actual, actual != member)
	return err
}

// ActivityEvents returns a page of activity after rechecking administrator authorization.
func (s *Service) ActivityEvents(ctx context.Context, principal Principal, query, kind, level string, page, size int) (domain.PaginatedResponse[ActivityEvent], error) {
	items := []ActivityEvent{}
	total := 0
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, principal); err != nil {
			return err
		}
		where := ` FROM member_activity_events e JOIN users u ON u.id=e.member_id LEFT JOIN users actor ON actor.id=e.actor_member_id WHERE ($1='' OR strpos(lower(unaccent(u.display_name || '#' || u.display_name_tag || ' ' || e.activity || ' ' || COALESCE(e.payload::text,'') || ' ' || COALESCE(actor.display_name,''))), lower(unaccent($1)))>0) AND ($2='' OR e.kind=$2) AND ($3='' OR e.log_level=$3)`
		if err := tx.QueryRow(ctx, `SELECT count(*)`+where, query, kind, level).Scan(&total); err != nil {
			return err
		}
		rows, err := tx.Query(ctx, `SELECT e.id::text,e.member_id::text,u.display_name,u.display_name_tag,e.occurred_at,e.kind,e.activity,e.log_level,e.payload,e.actor_member_id::text,actor.display_name,e.is_impersonated`+where+` ORDER BY e.occurred_at DESC,e.id DESC LIMIT $4 OFFSET $5`, query, kind, level, size, (int64(page)-1)*int64(size))
		if err != nil {
			return err
		}
		defer rows.Close()
		for rows.Next() {
			var event ActivityEvent
			if err := rows.Scan(&event.ID, &event.MemberID, &event.DisplayName, &event.DisplayNameTag, &event.OccurredAt, &event.Kind, &event.Activity, &event.LogLevel, &event.Payload, &event.ActorMemberID, &event.ActorDisplayName, &event.IsImpersonated); err != nil {
				return err
			}
			items = append(items, event)
		}
		return rows.Err()
	})
	return domain.Page(items, page, size, total), err
}
