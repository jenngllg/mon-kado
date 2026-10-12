package application

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
	"strings"
	"time"
)

// AdministrativeMemberAction revokes sessions or erases a different member with an atomic audit record.
func (s *Service) AdministrativeMemberAction(ctx context.Context, p Principal, target, reference string, erase bool) error {
	if p.MemberID == target {
		code := domain.AccountSelfSessionRevocationNotAllowed
		if erase {
			code = domain.AccountSelfErasureNotAllowed
		}
		return domain.Failure(409, code, "An administrator cannot target their own account through this operation.")
	}
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, p); err != nil {
			return err
		}
		var email string
		var confirmed bool
		err := tx.QueryRow(ctx, `SELECT email,email_confirmed FROM users WHERE id=$1 FOR UPDATE`, target).Scan(&email, &confirmed)
		if errors.Is(err, pgx.ErrNoRows) {
			code := domain.AccountSessionRevocationTargetNotFound
			if erase {
				code = domain.AccountErasureTargetNotFound
			}
			return missing(code)
		}
		if err != nil {
			return err
		}
		now, id := s.Now().UTC(), security.NewID()
		reference = strings.TrimSpace(reference)
		if !erase {
			if _, err = tx.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$2 WHERE user_id=$1 AND revoked_at IS NULL`, target, now); err != nil {
				return err
			}
			if _, err = tx.Exec(ctx, `UPDATE two_factor_challenges SET invalidated_at=$2 WHERE member_id=$1 AND consumed_at IS NULL AND invalidated_at IS NULL`, target, now); err != nil {
				return err
			}
			_, err = tx.Exec(ctx, `INSERT INTO administrative_session_revocation_events(id,administrator_id,member_id,request_reference,created_at) VALUES($1,$2,$3,$4,$5)`, id, p.MemberID, target, reference, now)
			return err
		}
		status := "NotApplicable"
		if confirmed {
			status = "Pending"
		}
		if _, err = tx.Exec(ctx, `INSERT INTO administrative_account_erasure_events(id,administrator_id,member_id,request_reference,created_at,notification_status) VALUES($1,$2,$3,$4,$5,$6)`, id, p.MemberID, target, reference, now, status); err != nil {
			return err
		}
		if confirmed {
			protected, err := s.Crypto.Protect(email, "erasure-recipient|"+id)
			if err != nil {
				return err
			}
			if _, err = tx.Exec(ctx, `INSERT INTO account_erasure_email_outbox(id,protected_recipient,created_at,expires_at,available_at,attempt_count) VALUES($1,$2,$3,$4,$3,0)`, id, protected, now, now.Add(24*time.Hour)); err != nil {
				return err
			}
		}
		return s.removeMember(ctx, tx, target)
	})
}

func (s *Service) authorizeAdministratorTx(ctx context.Context, tx pgx.Tx, p Principal) error {
	var allowed bool
	err := tx.QueryRow(ctx, `SELECT true FROM authentication_sessions session JOIN users u ON u.id=session.user_id JOIN member_two_factors f ON f.member_id=u.id JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id WHERE session.id=$1 AND u.id=$2 AND session.revoked_at IS NULL AND session.expires_at>$3 AND u.email_confirmed AND session.two_factor_verified_at IS NOT NULL AND session.two_factor_credential_id=f.credential_id AND r.normalized_name='ADMINISTRATOR' FOR SHARE OF session,u,f,ur`, p.SessionID, p.MemberID, s.Now().UTC()).Scan(&allowed)
	if errors.Is(err, pgx.ErrNoRows) {
		return domain.Failure(403, domain.SecurityForbidden, "Administrator authorization is no longer valid.")
	}
	return err
}

// AuditFilter contains centrally validated conjunctive filters.
type AuditFilter struct {
	Action                                  string
	Administrator, Member, Wishlist, Export *string
	Reference                               *string
	From, To                                *time.Time
}

const auditUnion = `WITH audit AS (
 SELECT id,occurred_at created_at,CASE action WHEN 'Suspended' THEN 'wishlistSuspended' WHEN 'ReasonUpdated' THEN 'wishlistSuspensionReasonUpdated' ELSE 'wishlistReactivated' END action,administrator_id,wishlist_id,NULL::uuid member_id,NULL::uuid export_id,reason,NULL::text request_reference FROM wishlist_moderation_events
 UNION ALL SELECT id,created_at,CASE action WHEN 'Requested' THEN 'memberDataExportRequested' ELSE 'memberDataExportDownloadStarted' END,administrator_id,NULL,member_id,export_id,NULL,request_reference FROM administrative_data_export_events
 UNION ALL SELECT id,created_at,'memberErased',administrator_id,NULL,member_id,NULL,NULL,request_reference FROM administrative_account_erasure_events
 UNION ALL SELECT id,created_at,'memberSessionsRevoked',administrator_id,NULL,member_id,NULL,NULL,request_reference FROM administrative_session_revocation_events)
 `

// AuditEvents reads a unified immutable audit page with half-open UTC boundaries.
func (s *Service) AuditEvents(ctx context.Context, filter AuditFilter, page, size int) (domain.PaginatedResponse[domain.AdministrativeAuditEventResponse], error) {
	items := []domain.AdministrativeAuditEventResponse{}
	ceil := func(value *time.Time) *time.Time {
		if value == nil {
			return nil
		}
		rounded := value.Truncate(time.Microsecond)
		if !rounded.Equal(*value) {
			rounded = rounded.Add(time.Microsecond)
		}
		return &rounded
	}
	parameters := []any{filter.Action, filter.Administrator, filter.Member, filter.Wishlist, filter.Export, filter.Reference, ceil(filter.From), ceil(filter.To)}
	where := ` FROM audit a LEFT JOIN users u ON u.id=a.administrator_id WHERE ($1='' OR a.action=$1) AND ($2::uuid IS NULL OR a.administrator_id=$2) AND ($3::uuid IS NULL OR a.member_id=$3) AND ($4::uuid IS NULL OR a.wishlist_id=$4) AND ($5::uuid IS NULL OR a.export_id=$5) AND ($6::text IS NULL OR a.request_reference COLLATE "C"=$6) AND ($7::timestamptz IS NULL OR a.created_at>=$7) AND ($8::timestamptz IS NULL OR a.created_at<$8)`
	var total int
	err := s.Store.Pool.QueryRow(ctx, auditUnion+`SELECT count(*)`+where, parameters...).Scan(&total)
	if err != nil || int64(page) > max(1, (int64(total)+int64(size)-1)/int64(size)) {
		return domain.Page(items, page, size, total), err
	}
	rows, err := s.Store.Pool.Query(ctx, auditUnion+`SELECT a.id::text,a.created_at,a.action,a.administrator_id::text,u.display_name,a.wishlist_id::text,a.member_id::text,a.export_id::text,a.reason,a.request_reference`+where+` ORDER BY a.created_at DESC,a.id DESC LIMIT $9 OFFSET $10`, append(parameters, size, (int64(page)-1)*int64(size))...)
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		var item domain.AdministrativeAuditEventResponse
		if err = rows.Scan(&item.Id, &item.CreatedAt, &item.Action, &item.AdministratorId, &item.AdministratorDisplayName, &item.WishlistId, &item.MemberId, &item.ExportId, &item.Reason, &item.RequestReference); err != nil {
			return domain.Page(items, page, size, total), err
		}
		items = append(items, item)
	}
	return domain.Page(items, page, size, total), rows.Err()
}
