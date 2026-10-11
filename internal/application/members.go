package application

import (
	"context"
	"encoding/json"
	"errors"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// SearchMembers searches confirmed display names without case or accent sensitivity.
func (s *Service) SearchMembers(ctx context.Context, displayName string, page, size int) (domain.PaginatedResponse[domain.UserSearchResponse], error) {
	items := []domain.UserSearchResponse{}
	var total int
	filter := `email_confirmed AND is_visible_in_member_search AND length(public.unaccent($1)) > 0 AND strpos(lower(public.unaccent(normalize(display_name,NFC))),lower(public.unaccent($1))) > 0`
	err := s.Store.Pool.QueryRow(ctx, `SELECT count(*) FROM users WHERE `+filter, strings.TrimSpace(displayName)).Scan(&total)
	if err != nil || total == 0 || int64(page) > (int64(total)+int64(size)-1)/int64(size) {
		return domain.Page(items, page, size, total), err
	}
	rows, err := s.Store.Pool.Query(ctx, `SELECT id::text,display_name,profile_image_id::text FROM users WHERE `+filter+` ORDER BY lower(public.unaccent(normalize(display_name,NFC))),id LIMIT $2 OFFSET $3`, strings.TrimSpace(displayName), size, (int64(page)-1)*int64(size))
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		var r domain.UserSearchResponse
		var image *string
		if err = rows.Scan(&r.Id, &r.DisplayName, &image); err != nil {
			return domain.Page(items, page, size, total), err
		}
		if image != nil {
			r.ProfileImageUrl = ptrString("/api/v1/members/" + r.Id + "/profile/image?imageId=" + *image)
		}
		items = append(items, r)
	}
	return domain.Page(items, page, size, total), rows.Err()
}

// RequestEmailChange verifies the password and version before queuing two atomic notifications.
func (s *Service) RequestEmailChange(ctx context.Context, user string, input domain.UpdateMemberEmailRequest, expected uint32) error {
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var currentEmail, stamp string
		var hash *string
		var version uint32
		if err := tx.QueryRow(ctx, `SELECT email,security_stamp,password_hash,xmin::text::bigint FROM users WHERE id=$1 FOR UPDATE`, user).Scan(&currentEmail, &stamp, &hash, &version); err != nil {
			return err
		}
		if hash == nil || !security.VerifyPassword(text(input.CurrentPassword), *hash) {
			return domain.Failure(400, domain.MemberCurrentPasswordInvalid, "The current password is invalid.")
		}
		if version != expected {
			return stale(domain.MemberProfileVersionConflict)
		}
		newEmail := text(input.Email)
		if strings.EqualFold(currentEmail, newEmail) {
			return domain.Failure(400, domain.RequestValidationError, "The new email must differ from the current email.")
		}
		var used bool
		if err := tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM users WHERE normalized_email=$1 AND id<>$2)`, strings.ToUpper(newEmail), user).Scan(&used); err != nil {
			return err
		}
		if used {
			return domain.Failure(409, domain.MemberEmailAlreadyUsed, "The email address is already used.")
		}
		now, requestID := s.Now().UTC(), security.NewID()
		if _, err := tx.Exec(ctx, `UPDATE member_email_change_requests SET revoked_at=$2 WHERE user_id=$1 AND confirmed_at IS NULL AND revoked_at IS NULL`, user, now); err != nil {
			return err
		}
		if _, err := tx.Exec(ctx, `INSERT INTO member_email_change_requests(id,user_id,current_email,new_email,normalized_new_email,created_at,expires_at) VALUES($1,$2,$3,$4,$5,$6,$7)`, requestID, user, currentEmail, newEmail, strings.ToUpper(newEmail), now, now.Add(24*time.Hour)); err != nil {
			return err
		}
		for _, kind := range []string{"EMAIL_CHANGE_CONFIRMATION", "EMAIL_CHANGE_SECURITY_NOTIFICATION"} {
			recipient := currentEmail
			var snapshot *string
			if kind == "EMAIL_CHANGE_CONFIRMATION" {
				recipient, snapshot = newEmail, &stamp
			}
			if _, err := tx.Exec(ctx, `INSERT INTO authentication_email_outbox(id,user_id,kind,created_at,available_at,attempt_count,recipient_email,security_stamp_snapshot,member_email_change_request_id) VALUES($1,$2,$3,$4,$4,0,$5,$6,$7)`, security.NewID(), user, kind, now, recipient, snapshot, requestID); err != nil {
				return err
			}
		}
		return nil
	})
	if duplicate(err, "ux_users_normalized_email") {
		return domain.Failure(409, domain.MemberEmailAlreadyUsed, "The email address is already used.")
	}
	return err
}

// ConfirmEmailChange atomically changes login identity and revokes all existing sessions.
func (s *Service) ConfirmEmailChange(ctx context.Context, input domain.ConfirmMemberEmailChangeRequest) error {
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var user, email string
		var expires time.Time
		var confirmed, revoked *time.Time
		err := tx.QueryRow(ctx, `SELECT user_id::text,new_email,expires_at,confirmed_at,revoked_at FROM member_email_change_requests WHERE id=$1 FOR UPDATE`, text(input.RequestId)).Scan(&user, &email, &expires, &confirmed, &revoked)
		if errors.Is(err, pgx.ErrNoRows) {
			return domain.Failure(400, domain.MemberEmailChangeInvalid, "The email change link is invalid.")
		}
		if err != nil {
			return err
		}
		if confirmed != nil || revoked != nil || !expires.After(s.Now()) {
			return domain.Failure(400, domain.MemberEmailChangeInvalid, "The email change link is invalid.")
		}
		payload, err := s.consumeAction(ctx, tx, user, "emailChange", text(input.Token), domain.MemberEmailChangeInvalid)
		if err != nil {
			return err
		}
		var action struct {
			RequestID string `json:"requestId"`
		}
		if err = json.Unmarshal(payload, &action); err != nil {
			return err
		}
		if action.RequestID != text(input.RequestId) {
			return domain.Failure(400, domain.MemberEmailChangeInvalid, "The email change link is invalid.")
		}
		now := s.Now().UTC()
		if _, err = tx.Exec(ctx, `UPDATE users SET email=$2,user_name=$2,normalized_email=$3,normalized_user_name=$3,security_stamp=$4,updated_at=$5 WHERE id=$1`, user, email, strings.ToUpper(email), security.RandomToken(), now); err != nil {
			return err
		}
		if _, err = tx.Exec(ctx, `UPDATE member_email_change_requests SET confirmed_at=$2 WHERE id=$1`, text(input.RequestId), now); err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$2 WHERE user_id=$1 AND revoked_at IS NULL`, user, now)
		return err
	})
	if duplicate(err, "ux_users_normalized_email") || duplicate(err, "ux_users_normalized_user_name") {
		return domain.Failure(409, domain.MemberEmailAlreadyUsed, "The email address is already used.")
	}
	return err
}

// RequestDeletion queues a short-lived confirmation with a durable per-account quota.
func (s *Service) RequestDeletion(ctx context.Context, user string) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var email, stamp string
		if err := tx.QueryRow(ctx, `SELECT email,security_stamp FROM users WHERE id=$1 FOR UPDATE`, user).Scan(&email, &stamp); err != nil {
			return err
		}
		now, id := s.Now().UTC(), security.NewID()
		var count int
		if err := tx.QueryRow(ctx, `SELECT count(*) FROM member_account_deletion_requests WHERE member_id=$1 AND created_at>$2`, user, now.Add(-time.Hour)).Scan(&count); err != nil {
			return err
		}
		if count >= 3 {
			return domain.Failure(429, domain.AccountDeletionRateLimited, "The account deletion request quota has been exceeded.")
		}
		if _, err := tx.Exec(ctx, `UPDATE member_account_deletion_requests SET expires_at=GREATEST($2,created_at+interval '1 microsecond') WHERE member_id=$1 AND expires_at>$2`, user, now); err != nil {
			return err
		}
		if _, err := tx.Exec(ctx, `INSERT INTO member_account_deletion_requests(id,member_id,email,security_stamp,created_at,expires_at) VALUES($1,$2,$3,$4,$5,$6)`, id, user, email, stamp, now, now.Add(30*time.Minute)); err != nil {
			return err
		}
		_, err := tx.Exec(ctx, `INSERT INTO authentication_email_outbox(id,user_id,kind,created_at,available_at,attempt_count,recipient_email,security_stamp_snapshot,member_account_deletion_request_id) VALUES($1,$2,'ACCOUNT_DELETION_CONFIRMATION',$3,$3,0,$4,$5,$6)`, security.NewID(), user, now, email, stamp, id)
		return err
	})
}

// ConfirmDeletion removes the member's personal data using a current account-bound proof.
func (s *Service) ConfirmDeletion(ctx context.Context, user string, input domain.ConfirmMemberAccountDeletionRequest) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		payload, err := s.consumeAction(ctx, tx, user, "accountDeletion", text(input.Token), domain.AccountDeletionInvalid)
		if err != nil {
			return err
		}
		var action struct {
			RequestID string `json:"requestId"`
		}
		if err = json.Unmarshal(payload, &action); err != nil {
			return err
		}
		var valid bool
		if err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM member_account_deletion_requests WHERE id=$1 AND member_id=$2 AND expires_at>$3)`, action.RequestID, user, s.Now().UTC()).Scan(&valid); err != nil {
			return err
		}
		if !valid {
			return domain.Failure(400, domain.AccountDeletionInvalid, "The account deletion link is invalid.")
		}
		return s.removeMember(ctx, tx, user)
	})
}

func (s *Service) removeMember(ctx context.Context, tx pgx.Tx, user string) error {
	rows, err := tx.Query(ctx, `SELECT id::text FROM wishlists WHERE owner_id=$1 FOR UPDATE`, user)
	if err != nil {
		return err
	}
	lists, err := pgx.CollectRows(rows, pgx.RowTo[string])
	if err != nil {
		return err
	}
	for _, id := range lists {
		if err = s.queueWishlistImages(ctx, tx, id); err != nil {
			return err
		}
	}
	if _, err = tx.Exec(ctx, `UPDATE gift_reservation_histories SET wishlist_name='Deleted wishlist',wish_name='Deleted gift' WHERE wishlist_id=ANY($1::uuid[]) AND member_id<>$2`, lists, user); err != nil {
		return err
	}
	var image *string
	if err = tx.QueryRow(ctx, `SELECT profile_image_id::text FROM users WHERE id=$1 FOR UPDATE`, user).Scan(&image); err != nil {
		return err
	}
	if image != nil {
		if err = s.queueImageDeletion(ctx, tx, *image); err != nil {
			return err
		}
	}
	if _, err = tx.Exec(ctx, `DELETE FROM wishlist_participants WHERE member_id=$1`, user); err != nil {
		return err
	}
	_, err = tx.Exec(ctx, `DELETE FROM users WHERE id=$1`, user)
	return err
}
