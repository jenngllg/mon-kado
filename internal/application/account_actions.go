package application

import (
	"context"
	"encoding/json"
	"errors"
	"net/url"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// EmailMessage is a provider-independent email without a dependency on a Gmail SDK.
type EmailMessage struct {
	To        string `json:"to"`
	Subject   string `json:"subject"`
	Text      string `json:"text"`
	ActionURL string `json:"actionUrl"`
}

// ErrEmailSuperseded identifies a stale action that must never be sent or retried.
var ErrEmailSuperseded = errors.New("authentication email action is superseded")

func (s *Service) issueAction(ctx context.Context, tx pgx.Tx, user, purpose, stamp string, payload any, lifetime time.Duration) (string, error) {
	token, now := security.RandomToken(), s.Now().UTC()
	data, err := json.Marshal(payload)
	if err != nil {
		return "", err
	}
	_, err = tx.Exec(ctx, `INSERT INTO account_action_tokens(id,user_id,purpose,token_hash,security_stamp,payload,created_at,expires_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8)`, security.NewID(), user, purpose, security.HashToken(token), stamp, data, now, now.Add(lifetime))
	return token, err
}

func (s *Service) consumeAction(ctx context.Context, tx pgx.Tx, user, purpose, token, code string) ([]byte, error) {
	var payload []byte
	var id string
	err := tx.QueryRow(ctx, `SELECT action.id::text,action.payload FROM account_action_tokens action JOIN users u ON u.id=action.user_id WHERE action.user_id=$1 AND action.purpose=$2 AND action.token_hash=$3 AND action.security_stamp=u.security_stamp AND action.consumed_at IS NULL AND action.expires_at>$4 FOR UPDATE OF action,u`, user, purpose, security.HashToken(token), s.Now().UTC()).Scan(&id, &payload)
	if errors.Is(err, pgx.ErrNoRows) {
		return nil, domain.Failure(400, code, "The confirmation link is invalid.")
	}
	if err != nil {
		return nil, err
	}
	_, err = tx.Exec(ctx, `UPDATE account_action_tokens SET consumed_at=$2 WHERE id=$1`, id, s.Now().UTC())
	return payload, err
}

// PrepareAuthenticationEmail creates and retains an encrypted message for explicit outbox attempts.
func (s *Service) PrepareAuthenticationEmail(ctx context.Context, outboxID string) (EmailMessage, error) {
	var message EmailMessage
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var user, kind, email, stamp string
		var protected, recipient, snapshot *string
		var confirmed bool
		err := tx.QueryRow(ctx, `SELECT o.user_id::text,o.kind,u.email,u.security_stamp,o.protected_payload,o.recipient_email,o.security_stamp_snapshot,u.email_confirmed FROM authentication_email_outbox o JOIN users u ON u.id=o.user_id WHERE o.id=$1 FOR UPDATE OF o`, outboxID).Scan(&user, &kind, &email, &stamp, &protected, &recipient, &snapshot, &confirmed)
		if err != nil {
			if errors.Is(err, pgx.ErrNoRows) {
				return ErrEmailSuperseded
			}
			return err
		}
		if snapshot != nil && *snapshot != stamp || kind == "EMAIL_CONFIRMATION" && confirmed || kind == "PASSWORD_RESET" && (!confirmed || text(recipient) != email) {
			return ErrEmailSuperseded
		}
		var eligible bool
		switch kind {
		case "EMAIL_CHANGE_CONFIRMATION":
			err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM member_email_change_requests r JOIN authentication_email_outbox o ON o.member_email_change_request_id=r.id WHERE o.id=$1 AND r.confirmed_at IS NULL AND r.revoked_at IS NULL AND r.expires_at>$2 AND r.new_email=o.recipient_email)`, outboxID, s.Now().UTC()).Scan(&eligible)
		case "ACCOUNT_DELETION_CONFIRMATION":
			err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM member_account_deletion_requests r JOIN authentication_email_outbox o ON o.member_account_deletion_request_id=r.id WHERE o.id=$1 AND r.expires_at>$2 AND r.security_stamp=$3 AND r.email=$4)`, outboxID, s.Now().UTC(), stamp, email).Scan(&eligible)
		case "PERSONAL_DATA_EXPORT_READY":
			err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM member_data_exports e JOIN authentication_email_outbox o ON o.member_data_export_id=e.id WHERE o.id=$1 AND e.member_id=$2 AND e.status='Ready' AND e.expires_at>$3)`, outboxID, user, s.Now().UTC()).Scan(&eligible)
		default:
			eligible = true
		}
		if err != nil {
			return err
		}
		if !eligible {
			return ErrEmailSuperseded
		}
		if protected != nil {
			plain, err := s.Crypto.Unprotect(*protected, "email|"+outboxID)
			if err != nil {
				return err
			}
			return json.Unmarshal([]byte(plain), &message)
		}
		if recipient != nil {
			email = *recipient
		}
		message = EmailMessage{To: email, Subject: "MonKado security notification", Text: "A security operation was performed on your MonKado account."}
		if kind == "PERSONAL_DATA_EXPORT_READY" {
			message.Subject = "Your MonKado personal data export is ready"
			message.Text = "Your private data export is ready and expires in 24 hours. Download it from " + s.Config.FrontendOrigin + "/profile"
		}
		if kind == "EMAIL_CONFIRMATION" || kind == "PASSWORD_RESET" {
			purpose, path, subject := "emailConfirmation", "/confirm-email", "Confirm your MonKado email"
			if kind == "PASSWORD_RESET" {
				purpose, path, subject = "passwordReset", "/reset-password", "Reset your MonKado password"
			}
			token, err := s.issueAction(ctx, tx, user, purpose, stamp, map[string]any{}, 24*time.Hour)
			if err != nil {
				return err
			}
			query := url.Values{"userId": {user}, "token": {token}}
			message.Subject = subject
			message.Text = s.Config.FrontendOrigin + path + "#" + query.Encode()
		}
		if kind == "EMAIL_CHANGE_CONFIRMATION" || kind == "ACCOUNT_DELETION_CONFIRMATION" {
			var requestID string
			purpose, path, lifetime := "emailChange", "/confirm-email-change", 24*time.Hour
			if kind == "EMAIL_CHANGE_CONFIRMATION" {
				err = tx.QueryRow(ctx, `SELECT member_email_change_request_id::text FROM authentication_email_outbox WHERE id=$1`, outboxID).Scan(&requestID)
			}
			if kind == "ACCOUNT_DELETION_CONFIRMATION" {
				purpose, path, lifetime = "accountDeletion", "/account/delete/confirm", 30*time.Minute
				err = tx.QueryRow(ctx, `SELECT member_account_deletion_request_id::text FROM authentication_email_outbox WHERE id=$1`, outboxID).Scan(&requestID)
			}
			if err != nil {
				return err
			}
			token, err := s.issueAction(ctx, tx, user, purpose, stamp, map[string]string{"requestId": requestID}, lifetime)
			if err != nil {
				return err
			}
			query := url.Values{"requestId": {requestID}, "token": {token}}
			message.Subject = "Confirm your MonKado account operation"
			message.Text = s.Config.FrontendOrigin + path + "#" + query.Encode()
		}
		message = composeEmail(kind, message, s.Config.FrontendOrigin, s.Config.Jobs().ExportLifetime)
		data, err := json.Marshal(message)
		if err != nil {
			return err
		}
		encoded, err := s.Crypto.Protect(string(data), "email|"+outboxID)
		if err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `UPDATE authentication_email_outbox SET protected_payload=$2 WHERE id=$1`, outboxID, encoded)
		return err
	})
	return message, err
}

// RequestAccountEmail silently queues confirmation or reset messages for eligible accounts.
func (s *Service) RequestAccountEmail(ctx context.Context, email string, reset bool) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var id, recipient, stamp string
		var confirmed bool
		err := tx.QueryRow(ctx, `SELECT id::text,email,security_stamp,email_confirmed FROM users WHERE normalized_email=$1 FOR UPDATE`, strings.ToUpper(email)).Scan(&id, &recipient, &stamp, &confirmed)
		if errors.Is(err, pgx.ErrNoRows) {
			return nil
		}
		if err != nil {
			return err
		}
		if reset && !confirmed || !reset && confirmed {
			return nil
		}
		kind := "EMAIL_CONFIRMATION"
		if reset {
			kind = "PASSWORD_RESET"
		}
		var recent bool
		if err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM authentication_email_outbox WHERE user_id=$1 AND kind=$2 AND created_at>$3)`, id, kind, s.Now().UTC().Add(-5*time.Minute)).Scan(&recent); err != nil {
			return err
		}
		if recent {
			return nil
		}
		if reset {
			return s.queueEmail(ctx, tx, id, kind, &recipient, &stamp)
		}
		return s.queueEmail(ctx, tx, id, kind, nil, nil)
	})
}

// ConfirmEmail confirms one time-limited, account-bound email proof.
func (s *Service) ConfirmEmail(ctx context.Context, request domain.ConfirmEmailRequest) error {
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		id := text(request.UserId)
		if _, err := s.consumeAction(ctx, tx, id, "emailConfirmation", text(request.Token), domain.AccountEmailConfirmationInvalid); err != nil {
			return err
		}
		_, err := tx.Exec(ctx, `UPDATE users SET email_confirmed=true,unconfirmed_account_expires_at=NULL,updated_at=$2 WHERE id=$1`, id, s.Now().UTC())
		return err
	})
}

// ResetPassword changes credentials and revokes every access and refresh session atomically.
func (s *Service) ResetPassword(ctx context.Context, request domain.ResetPasswordRequest) error {
	hash := security.HashPassword(text(request.NewPassword))
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		id := text(request.UserId)
		if _, err := s.consumeAction(ctx, tx, id, "passwordReset", text(request.Token), domain.AccountPasswordResetInvalid); err != nil {
			return err
		}
		return s.replacePassword(ctx, tx, id, hash)
	})
}

func (s *Service) replacePassword(ctx context.Context, tx pgx.Tx, user, hash string) error {
	var email string
	now := s.Now().UTC()
	if err := tx.QueryRow(ctx, `UPDATE users SET password_hash=$2,security_stamp=$3,access_failed_count=0,lockout_end=NULL,updated_at=$4 WHERE id=$1 RETURNING email`, user, hash, security.RandomToken(), now).Scan(&email); err != nil {
		return err
	}
	if _, err := tx.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$2 WHERE user_id=$1 AND revoked_at IS NULL`, user, now); err != nil {
		return err
	}
	return s.queueEmail(ctx, tx, user, "PASSWORD_CHANGED_SECURITY_NOTIFICATION", &email, nil)
}

// UpdatePassword requires the current credential and prohibits selecting the same password.
func (s *Service) UpdatePassword(ctx context.Context, user string, request domain.UpdateMemberPasswordRequest) error {
	hash := security.HashPassword(text(request.NewPassword))
	return s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var current *string
		if err := tx.QueryRow(ctx, `SELECT password_hash FROM users WHERE id=$1 FOR UPDATE`, user).Scan(&current); err != nil {
			return err
		}
		if current == nil || !security.VerifyPassword(text(request.CurrentPassword), *current) {
			return domain.Failure(400, domain.MemberCurrentPasswordInvalid, "The current password is invalid.")
		}
		if security.VerifyPassword(text(request.NewPassword), *current) {
			return &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The new password must differ from the current password.", Validation: []domain.ValidationError{{PropertyName: "newPassword", ErrorMessage: "The new password must differ from the current password."}}}
		}
		return s.replacePassword(ctx, tx, user, hash)
	})
}

// UpdateProfile changes profile preferences using the profile's optimistic version.
func (s *Service) UpdateProfile(ctx context.Context, user string, request domain.UpdateMemberProfileRequest, version uint32) (domain.MemberProfileResponse, uint32, error) {
	var result domain.MemberProfileResponse
	var current uint32
	var image *string
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		err := tx.QueryRow(ctx, `SELECT display_name,display_name_tag,profile_image_id::text,xmin::text::bigint,is_visible_in_member_search FROM users WHERE id=$1 FOR UPDATE`, user).Scan(&result.DisplayName, &result.DisplayNameTag, &image, &current, &result.IsVisibleInMemberSearch)
		if errors.Is(err, pgx.ErrNoRows) {
			return unauthorized()
		}
		if err != nil {
			return err
		}
		if current != version {
			return stale(domain.MemberProfileVersionConflict)
		}
		name := strings.TrimSpace(text(request.DisplayName))
		if result.DisplayName == name && (request.IsVisibleInMemberSearch == nil || result.IsVisibleInMemberSearch == *request.IsVisibleInMemberSearch) {
			return nil
		}
		return tx.QueryRow(ctx, `UPDATE users SET display_name=$2,updated_at=$3,is_visible_in_member_search=COALESCE($4,is_visible_in_member_search) WHERE id=$1 RETURNING display_name,display_name_tag,profile_image_id::text,xmin::text::bigint,is_visible_in_member_search`, user, name, s.Now().UTC(), request.IsVisibleInMemberSearch).Scan(&result.DisplayName, &result.DisplayNameTag, &image, &current, &result.IsVisibleInMemberSearch)
	})
	if image != nil {
		result.ProfileImageUrl = ptrString("/api/v1/members/" + user + "/profile/image?imageId=" + *image)
	}
	return result, current, memberTagError(err)
}

func ptrString(value string) *string { return &value }
