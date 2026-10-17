package application

import (
	"context"
	"errors"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// SessionResult includes browser refresh material separately from the public access response.
type SessionResult struct {
	Access       domain.AccessTokenResponse
	RefreshToken string
	ExpiresAt    time.Time
	Persistent   bool
	Challenge    *domain.TwoFactorChallengeResponse
}

// Register atomically creates an unconfirmed member and queues its email without disclosing duplicates.
func (s *Service) Register(ctx context.Context, request domain.RegisterAccountRequest) error {
	password := security.HashPassword(text(request.Password))
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		id, now := security.NewID(), s.Now().UTC()
		_, err := tx.Exec(ctx, `INSERT INTO users(id,display_name,created_at,updated_at,unconfirmed_account_expires_at,user_name,normalized_user_name,email,normalized_email,email_confirmed,password_hash,security_stamp,concurrency_stamp,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) VALUES($1,$2,$3,$3,$4,$5,$6,$5,$6,false,$7,$8,$9,false,false,true,0)`, id, strings.TrimSpace(text(request.DisplayName)), now, now.Add(30*24*time.Hour), text(request.Email), strings.ToUpper(text(request.Email)), password, security.RandomToken(), security.NewID())
		if err != nil {
			return err
		}
		if _, err = tx.Exec(ctx, `INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE normalized_name='MEMBER'`, id); err != nil {
			return err
		}
		return s.queueEmail(ctx, tx, id, "EMAIL_CONFIRMATION", nil, nil)
	})
	if duplicate(err, "ux_users_normalized_email") || duplicate(err, "ux_users_normalized_user_name") {
		return nil
	}
	return memberTagError(err)
}

func (s *Service) queueEmail(ctx context.Context, tx pgx.Tx, user, kind string, recipient, stamp *string) error {
	now := s.Now().UTC()
	_, err := tx.Exec(ctx, `INSERT INTO authentication_email_outbox(id,user_id,kind,created_at,available_at,attempt_count,recipient_email,security_stamp_snapshot) VALUES($1,$2,$3,$4,$4,0,$5,$6)`, security.NewID(), user, kind, now, recipient, stamp)
	return err
}

// Login validates credentials under an account lock and replaces only a proven browser session.
func (s *Service) Login(ctx context.Context, request domain.LoginRequest, currentRefresh string) (SessionResult, error) {
	var result SessionResult
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var id string
		var password *string
		var confirmed bool
		var lockout *time.Time
		var failures int
		err := tx.QueryRow(ctx, `SELECT id::text,password_hash,email_confirmed,lockout_end,access_failed_count FROM users WHERE normalized_email=$1 FOR UPDATE`, strings.ToUpper(text(request.Email))).Scan(&id, &password, &confirmed, &lockout, &failures)
		if errors.Is(err, pgx.ErrNoRows) {
			security.HashPassword(text(request.Password))
			return domain.Failure(401, domain.AccountInvalidCredentials, "The credentials are invalid.")
		}
		if err != nil {
			return err
		}
		now := s.Now().UTC()
		if lockout != nil && lockout.After(now) {
			result.Access.TokenType = "invalid"
			return nil
		}
		if password == nil || !security.VerifyPassword(text(request.Password), text(password)) {
			failures++
			var until *time.Time
			if failures >= 5 {
				t := now.Add(5 * time.Minute)
				until = &t
				failures = 0
			}
			if _, err = tx.Exec(ctx, `UPDATE users SET access_failed_count=$2,lockout_end=$3,updated_at=$4 WHERE id=$1`, id, failures, until, now); err != nil {
				return err
			}
			// Commit lockout counters before returning the public credential failure.
			result.Access.TokenType = "invalid"
			return nil
		}
		if !confirmed {
			return domain.Failure(403, domain.AccountEmailNotConfirmed, "The email address has not been confirmed.")
		}
		var upgradedPassword *string
		if security.PasswordNeedsRehash(*password) {
			value := security.HashPassword(text(request.Password))
			upgradedPassword = &value
		}
		// Upgrade only a proven credential under the same account lock as session creation.
		if _, err = tx.Exec(ctx, `UPDATE users SET access_failed_count=0,lockout_end=NULL,password_hash=COALESCE($2,password_hash) WHERE id=$1`, id, upgradedPassword); err != nil {
			return err
		}
		var requiresTwoFactor bool
		if err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$1 AND r.normalized_name='ADMINISTRATOR') OR EXISTS(SELECT 1 FROM member_two_factors WHERE member_id=$1 AND credential_id IS NOT NULL)`, id).Scan(&requiresTwoFactor); err != nil {
			return err
		}
		if requiresTwoFactor {
			var previous *string
			if old, parseErr := security.SessionID(currentRefresh); parseErr == nil {
				var proven string
				proofErr := tx.QueryRow(ctx, `SELECT id::text FROM authentication_sessions WHERE id=$1 AND user_id=$2 AND refresh_token_hash=$3 AND revoked_at IS NULL`, old, id, security.HashToken(currentRefresh)).Scan(&proven)
				if proofErr != nil && !errors.Is(proofErr, pgx.ErrNoRows) {
					return proofErr
				}
				if proofErr == nil {
					previous = &proven
				}
			}
			challenge, err := s.beginFactor(ctx, tx, id, "SignIn", request.RememberMe, nil, previous)
			result.Challenge = &challenge
			return err
		}
		if old, err := security.SessionID(currentRefresh); err == nil {
			if _, err = tx.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$3 WHERE id=$1 AND user_id=$2 AND refresh_token_hash=$4 AND revoked_at IS NULL`, old, id, now, security.HashToken(currentRefresh)); err != nil {
				return err
			}
		}
		result, err = s.createSession(ctx, tx, id, request.RememberMe, nil)
		return err
	})
	if err == nil && result.Access.TokenType == "invalid" {
		err = domain.Failure(401, domain.AccountInvalidCredentials, "The credentials are invalid.")
	}
	return result, err
}

func (s *Service) createSession(ctx context.Context, tx pgx.Tx, user string, persistent bool, twoFactorID *string) (SessionResult, error) {
	id, now := security.NewID(), s.Now().UTC()
	lifetime := 8 * time.Hour
	if persistent {
		lifetime = 30 * 24 * time.Hour
	}
	result := SessionResult{RefreshToken: security.SessionToken(id), ExpiresAt: now.Add(lifetime), Persistent: persistent}
	var verified *time.Time
	if twoFactorID != nil {
		verified = &now
	}
	_, err := tx.Exec(ctx, `INSERT INTO authentication_sessions(id,user_id,refresh_token_hash,created_at,renewed_at,expires_at,is_persistent,two_factor_credential_id,two_factor_verified_at) VALUES($1,$2,$3,$4,$4,$5,$6,$7,$8)`, id, user, security.HashToken(result.RefreshToken), now, result.ExpiresAt, persistent, twoFactorID, verified)
	if err != nil {
		return result, err
	}
	result.Access, err = s.issueAccess(ctx, tx, user, id)
	return result, err
}

func (s *Service) issueAccess(ctx context.Context, tx pgx.Tx, user, session string) (domain.AccessTokenResponse, error) {
	id, now := security.NewID(), s.Now().UTC()
	value, err := s.Crypto.AccessToken(user, id)
	if err != nil {
		return domain.AccessTokenResponse{}, err
	}
	_, err = tx.Exec(ctx, `INSERT INTO authentication_access_tokens(id,session_id,issued_at,expires_at) VALUES($1,$2,$3,$4)`, id, session, now, now.Add(15*time.Minute))
	return domain.AccessTokenResponse{AccessToken: value, TokenType: "Bearer", ExpiresIn: 900}, err
}

// Refresh serializes token rotation and rejects expired, revoked and replayed tokens.
func (s *Service) Refresh(ctx context.Context, token string) (SessionResult, error) {
	var result SessionResult
	id, err := security.SessionID(token)
	if err != nil {
		return result, unauthorized()
	}
	err = s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var user string
		var hash []byte
		var revoked *time.Time
		var confirmed bool
		err := tx.QueryRow(ctx, `SELECT session.user_id::text,session.refresh_token_hash,session.expires_at,session.is_persistent,session.revoked_at,u.email_confirmed FROM authentication_sessions session JOIN users u ON u.id=session.user_id WHERE session.id=$1 FOR UPDATE OF session`, id).Scan(&user, &hash, &result.ExpiresAt, &result.Persistent, &revoked, &confirmed)
		if errors.Is(err, pgx.ErrNoRows) {
			return unauthorized()
		}
		if err != nil {
			return err
		}
		now := s.Now().UTC()
		if revoked != nil || !result.ExpiresAt.After(now) || !confirmed || !security.VerifyToken(token, hash) {
			return unauthorized()
		}
		result.RefreshToken = security.SessionToken(id)
		if !result.Persistent {
			result.ExpiresAt = now.Add(8 * time.Hour)
		}
		if _, err = tx.Exec(ctx, `UPDATE authentication_sessions SET refresh_token_hash=$2,renewed_at=$3,expires_at=$4 WHERE id=$1`, id, security.HashToken(result.RefreshToken), now, result.ExpiresAt); err != nil {
			return err
		}
		result.Access, err = s.issueAccess(ctx, tx, user, id)
		return err
	})
	return result, err
}

// Logout revokes only the browser session whose token the caller proves.
func (s *Service) Logout(ctx context.Context, token string) error {
	id, err := security.SessionID(token)
	if err != nil {
		return nil
	}
	_, err = s.Store.Pool.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$3 WHERE id=$1 AND refresh_token_hash=$2 AND revoked_at IS NULL`, id, security.HashToken(token), s.Now().UTC())
	return err
}

// CurrentSession returns the server-side identity and profile version.
func (s *Service) CurrentSession(ctx context.Context, principal Principal) (domain.CurrentSessionResponse, uint32, error) {
	r := domain.CurrentSessionResponse{Roles: principal.Roles}
	var version uint32
	var image *string
	err := s.Store.Pool.QueryRow(ctx, `SELECT id::text,email,display_name,display_name_tag,profile_image_id::text,xmin::text::bigint,EXISTS(SELECT 1 FROM user_logins WHERE user_id=users.id AND login_provider='Google'),is_visible_in_member_search FROM users WHERE id=$1`, principal.MemberID).Scan(&r.Id, &r.Email, &r.DisplayName, &r.DisplayNameTag, &image, &version, &r.IsGoogleLinked, &r.IsVisibleInMemberSearch)
	if image != nil {
		url := "/api/v1/members/" + r.Id + "/profile/image?imageId=" + *image
		r.ProfileImageUrl = &url
	}
	return r, version, err
}
