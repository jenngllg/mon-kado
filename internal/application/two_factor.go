package application

import (
	"context"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"net/url"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

type factorState struct {
	GoogleContext                                                                             *string
	Confirmed                                                                                 bool
	User, Stamp, Email, ID, Purpose, Action                                                   string
	Credential, Protected, Expected, PendingID, PendingSecret, Management, Previous, Reserved *string
	Last                                                                                      *int64
	Failed, Count                                                                             int
	Locked, Window                                                                            *time.Time
	Expires                                                                                   time.Time
	Persistent                                                                                bool
}

func factorFailure() error {
	return domain.Failure(401, domain.AccountTwoFactorAuthenticationFailed, "The second-factor proof is invalid.")
}
func factorConflict() error {
	return domain.Failure(409, domain.AccountTwoFactorOperationConflict, "The flow cannot perform this operation.")
}

func (s *Service) beginFactor(ctx context.Context, tx pgx.Tx, user, purpose string, persistent bool, management, previous *string) (domain.TwoFactorChallengeResponse, error) {
	var credential *string
	var stamp string
	if _, err := tx.Exec(ctx, `INSERT INTO member_two_factors(member_id,failed_attempts,verification_count) VALUES($1,0,0) ON CONFLICT DO NOTHING`, user); err != nil {
		return domain.TwoFactorChallengeResponse{}, err
	}
	if err := tx.QueryRow(ctx, `SELECT u.security_stamp,f.credential_id::text FROM users u JOIN member_two_factors f ON f.member_id=u.id WHERE u.id=$1`, user).Scan(&stamp, &credential); err != nil {
		return domain.TwoFactorChallengeResponse{}, err
	}
	action := "Verify"
	if credential == nil {
		action = "Enroll"
	}
	if purpose == "ReplaceAuthenticator" {
		action = "Replace"
	}
	if purpose == "RegenerateRecoveryCodes" {
		action = "Complete"
	}
	flow, id, now := security.RandomToken(), security.NewID(), s.Now().UTC()
	expiry := now.Add(5 * time.Minute)
	_, err := tx.Exec(ctx, `INSERT INTO two_factor_challenges(id,member_id,flow_hash,purpose,required_action,created_at,expires_at,expected_credential_id,security_stamp_hash,is_persistent,management_session_id,previous_session_id) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)`, id, user, security.HashToken(flow), purpose, action, now, expiry, credential, security.HashToken(stamp), persistent, management, previous)
	return domain.TwoFactorChallengeResponse{Flow: flow, RequiredAction: lowerFirst(action), ExpiresAt: expiry}, err
}

func (s *Service) loadFactor(ctx context.Context, tx pgx.Tx, user string) (factorState, error) {
	var f factorState
	f.User = user
	if err := tx.QueryRow(ctx, `SELECT security_stamp,email,email_confirmed FROM users WHERE id=$1 FOR UPDATE`, user).Scan(&f.Stamp, &f.Email, &f.Confirmed); err != nil {
		if errors.Is(err, pgx.ErrNoRows) {
			return f, factorFailure()
		}
		return f, err
	}
	err := tx.QueryRow(ctx, `SELECT credential_id::text,protected_secret,last_accepted_time_step,failed_attempts,locked_until,verification_window_started_at,verification_count FROM member_two_factors WHERE member_id=$1 FOR UPDATE`, user).Scan(&f.Credential, &f.Protected, &f.Last, &f.Failed, &f.Locked, &f.Window, &f.Count)
	if errors.Is(err, pgx.ErrNoRows) {
		return f, factorFailure()
	}
	return f, err
}

func (s *Service) factorFlow(ctx context.Context, flow string, p Principal, operation func(pgx.Tx, *factorState) error) error {
	var rejected error
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var user string
		if err := tx.QueryRow(ctx, `SELECT member_id::text FROM two_factor_challenges WHERE flow_hash=$1`, security.HashToken(flow)).Scan(&user); err != nil {
			if errors.Is(err, pgx.ErrNoRows) {
				return factorFailure()
			}
			return err
		}
		f, err := s.loadFactor(ctx, tx, user)
		if err != nil {
			return err
		}
		var stamp []byte
		err = tx.QueryRow(ctx, `SELECT id::text,purpose,required_action,expires_at,expected_credential_id::text,security_stamp_hash,is_persistent,pending_credential_id::text,pending_protected_secret,management_session_id::text,previous_session_id::text,reserved_recovery_code_id::text,protected_google_context FROM two_factor_challenges WHERE flow_hash=$1 AND consumed_at IS NULL AND invalidated_at IS NULL FOR UPDATE`, security.HashToken(flow)).Scan(&f.ID, &f.Purpose, &f.Action, &f.Expires, &f.Expected, &stamp, &f.Persistent, &f.PendingID, &f.PendingSecret, &f.Management, &f.Previous, &f.Reserved, &f.GoogleContext)
		if errors.Is(err, pgx.ErrNoRows) {
			return factorFailure()
		}
		if err != nil {
			return err
		}
		if !f.Expires.After(s.Now()) || text(f.Expected) != text(f.Credential) || subtle.ConstantTimeCompare(stamp, security.HashToken(f.Stamp)) != 1 || !f.Confirmed && f.GoogleContext == nil {
			return factorFailure()
		}
		if f.Purpose != "SignIn" {
			if f.Management == nil || p.MemberID != user || p.SessionID != *f.Management {
				return factorFailure()
			}
			var live bool
			if err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM authentication_sessions WHERE id=$1 AND user_id=$2 AND revoked_at IS NULL AND expires_at>$3)`, f.Management, user, s.Now().UTC()).Scan(&live); err != nil {
				return err
			}
			if !live {
				return factorFailure()
			}
		}
		err = operation(tx, &f)
		var business *domain.Error
		if errors.As(err, &business) {
			rejected = err
			return nil
		}
		return err
	})
	if err != nil {
		return err
	}
	return rejected
}

func (s *Service) startFactorVerification(ctx context.Context, tx pgx.Tx, f *factorState) error {
	now := s.Now().UTC()
	if f.Locked != nil && f.Locked.After(now) {
		return domain.Failure(429, domain.AccountTwoFactorRateLimited, "Second-factor verification is temporarily locked.")
	}
	if f.Window == nil || !f.Window.Add(time.Minute).After(now) {
		f.Window = &now
		f.Count = 0
	}
	if f.Count >= 10 {
		return domain.Failure(429, domain.AccountTwoFactorRateLimited, "Too many second-factor verification attempts.")
	}
	f.Count++
	_, err := tx.Exec(ctx, `UPDATE member_two_factors SET verification_window_started_at=$2,verification_count=$3 WHERE member_id=$1`, f.User, f.Window, f.Count)
	return err
}

func (s *Service) factorFailed(ctx context.Context, tx pgx.Tx, f *factorState) error {
	f.Failed++
	var until *time.Time
	if f.Failed >= 5 {
		t := s.Now().UTC().Add(15 * time.Minute)
		until = &t
		f.Failed = 0
	}
	if _, err := tx.Exec(ctx, `UPDATE member_two_factors SET failed_attempts=$2,locked_until=$3 WHERE member_id=$1`, f.User, f.Failed, until); err != nil {
		return err
	}
	return factorFailure()
}

func (s *Service) verifyCurrentFactor(ctx context.Context, tx pgx.Tx, f *factorState, code string) error {
	if f.Credential == nil || f.Protected == nil {
		return factorConflict()
	}
	secret, err := s.Crypto.Unprotect(*f.Protected, "authenticator|"+f.User+"|"+*f.Credential)
	if err != nil {
		return err
	}
	step, valid := security.VerifyAuthenticator(secret, code, s.Now(), f.Last)
	if !valid {
		return s.factorFailed(ctx, tx, f)
	}
	_, err = tx.Exec(ctx, `UPDATE member_two_factors SET last_accepted_time_step=$2,failed_attempts=0,locked_until=NULL WHERE member_id=$1`, f.User, step)
	return err
}

func (s *Service) reserveRecovery(ctx context.Context, tx pgx.Tx, f *factorState, code string) error {
	var id string
	err := tx.QueryRow(ctx, `UPDATE two_factor_recovery_codes SET reserved_challenge_id=$3,reserved_until=$4 WHERE member_id=$1 AND credential_id=$2 AND code_hash=$5 AND consumed_at IS NULL AND (reserved_until IS NULL OR reserved_until<=$6) RETURNING id::text`, f.User, f.Credential, f.ID, f.Expires, security.HashToken(security.NormalizeRecoveryCode(code)), s.Now().UTC()).Scan(&id)
	if errors.Is(err, pgx.ErrNoRows) {
		return s.factorFailed(ctx, tx, f)
	}
	if err != nil {
		return err
	}
	f.Reserved = &id
	_, err = tx.Exec(ctx, `UPDATE two_factor_challenges SET reserved_recovery_code_id=$2,required_action='Replace' WHERE id=$1`, f.ID, id)
	if err != nil {
		return err
	}
	f.Action = "Replace"
	return s.queueEmail(ctx, tx, f.User, "TWO_FACTOR_RECOVERY_CODE_USED", &f.Email, nil)
}

// TwoFactorSetup discloses an encrypted candidate only to its live, session-bound flow.
func (s *Service) TwoFactorSetup(ctx context.Context, flow string, p Principal) (domain.TwoFactorSetupResponse, error) {
	var result domain.TwoFactorSetupResponse
	err := s.factorFlow(ctx, flow, p, func(tx pgx.Tx, f *factorState) error {
		if f.Action != "Enroll" && f.Action != "Replace" {
			return factorConflict()
		}
		if f.PendingID == nil {
			id, secret := security.NewID(), security.AuthenticatorSecret()
			protected, err := s.Crypto.Protect(secret, "authenticator|"+f.User+"|"+id)
			if err != nil {
				return err
			}
			if _, err = tx.Exec(ctx, `UPDATE two_factor_challenges SET pending_credential_id=$2,pending_protected_secret=$3 WHERE id=$1`, f.ID, id, protected); err != nil {
				return err
			}
			f.PendingID = &id
			f.PendingSecret = &protected
		}
		secret, err := s.Crypto.Unprotect(text(f.PendingSecret), "authenticator|"+f.User+"|"+text(f.PendingID))
		if err != nil {
			return err
		}
		result = domain.TwoFactorSetupResponse{ManualKey: secret, OtpAuthUri: "otpauth://totp/" + url.PathEscape("MonKado:"+f.User) + "?secret=" + secret + "&issuer=MonKado&algorithm=SHA1&digits=6&period=30"}
		return nil
	})
	return result, err
}

func (s *Service) recoveryCodes(ctx context.Context, tx pgx.Tx, f *factorState) ([]string, error) {
	if _, err := tx.Exec(ctx, `DELETE FROM two_factor_recovery_codes WHERE member_id=$1`, f.User); err != nil {
		return nil, err
	}
	codes := make([]string, 10)
	for i := range codes {
		codes[i] = security.RecoveryCode()
		if _, err := tx.Exec(ctx, `INSERT INTO two_factor_recovery_codes(id,member_id,credential_id,code_hash) VALUES($1,$2,$3,$4)`, security.NewID(), f.User, f.Credential, security.HashToken(security.NormalizeRecoveryCode(codes[i]))); err != nil {
			return nil, err
		}
	}
	return codes, nil
}

func (s *Service) revokeFactorProofs(ctx context.Context, tx pgx.Tx, f *factorState) error {
	now := s.Now().UTC()
	if _, err := tx.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$2 WHERE user_id=$1 AND revoked_at IS NULL`, f.User, now); err != nil {
		return err
	}
	_, err := tx.Exec(ctx, `UPDATE two_factor_challenges SET invalidated_at=$3 WHERE member_id=$1 AND id<>$2 AND consumed_at IS NULL AND invalidated_at IS NULL`, f.User, f.ID, now)
	return err
}

// ConfirmTwoFactorSetup installs the candidate once, invalidates other proofs and returns recovery codes once.
func (s *Service) ConfirmTwoFactorSetup(ctx context.Context, flow, code string, p Principal) (domain.TwoFactorRecoveryCodesResponse, error) {
	var result domain.TwoFactorRecoveryCodesResponse
	err := s.factorFlow(ctx, flow, p, func(tx pgx.Tx, f *factorState) error {
		if (f.Action != "Enroll" && f.Action != "Replace") || f.PendingID == nil || f.PendingSecret == nil {
			return factorConflict()
		}
		if err := s.startFactorVerification(ctx, tx, f); err != nil {
			return err
		}
		secret, err := s.Crypto.Unprotect(*f.PendingSecret, "authenticator|"+f.User+"|"+*f.PendingID)
		if err != nil {
			return err
		}
		step, valid := security.VerifyAuthenticator(secret, code, s.Now(), nil)
		if !valid {
			return s.factorFailed(ctx, tx, f)
		}
		if f.Reserved != nil {
			command, err := tx.Exec(ctx, `UPDATE two_factor_recovery_codes SET consumed_at=$3 WHERE id=$1 AND reserved_challenge_id=$2 AND reserved_until>$3 AND consumed_at IS NULL`, f.Reserved, f.ID, s.Now().UTC())
			if err != nil {
				return err
			}
			if command.RowsAffected() != 1 {
				return factorFailure()
			}
		}
		kind := "TWO_FACTOR_ENROLLED"
		if f.Credential != nil {
			kind = "TWO_FACTOR_REPLACED"
		}
		now := s.Now().UTC()
		if _, err = tx.Exec(ctx, `UPDATE member_two_factors SET credential_id=$2,protected_secret=$3,enabled_at=$4,last_accepted_time_step=$5,failed_attempts=0,locked_until=NULL WHERE member_id=$1`, f.User, f.PendingID, f.PendingSecret, now, step); err != nil {
			return err
		}
		if _, err = tx.Exec(ctx, `UPDATE users SET two_factor_enabled=true WHERE id=$1`, f.User); err != nil {
			return err
		}
		f.Credential = f.PendingID
		result.RecoveryCodes, err = s.recoveryCodes(ctx, tx, f)
		if err != nil {
			return err
		}
		var consumed *time.Time
		if f.Purpose != "SignIn" {
			consumed = &now
		}
		if _, err = tx.Exec(ctx, `UPDATE two_factor_challenges SET expected_credential_id=$2,required_action='Complete',verified_at=$3,consumed_at=$4,pending_credential_id=NULL,pending_protected_secret=NULL WHERE id=$1`, f.ID, f.Credential, now, consumed); err != nil {
			return err
		}
		if err = s.revokeFactorProofs(ctx, tx, f); err != nil {
			return err
		}
		return s.queueEmail(ctx, tx, f.User, kind, &f.Email, nil)
	})
	return result, err
}

// CompleteTwoFactor creates a browser session only after the required factor has completed.
func (s *Service) CompleteTwoFactor(ctx context.Context, input domain.TwoFactorCompletionRequest, p Principal) (SessionResult, error) {
	var result SessionResult
	err := s.factorFlow(ctx, text(input.Flow), p, func(tx pgx.Tx, f *factorState) error {
		if f.Purpose != "SignIn" {
			return factorConflict()
		}
		if f.Action == "Verify" {
			if err := s.startFactorVerification(ctx, tx, f); err != nil {
				return err
			}
			if input.RecoveryCode != nil {
				if err := s.reserveRecovery(ctx, tx, f, *input.RecoveryCode); err != nil {
					return err
				}
				result.Challenge = &domain.TwoFactorChallengeResponse{Flow: text(input.Flow), RequiredAction: "replace", ExpiresAt: f.Expires}
				return nil
			}
			if err := s.verifyCurrentFactor(ctx, tx, f, text(input.Code)); err != nil {
				return err
			}
			f.Action = "Complete"
		} else if input.Code != nil || input.RecoveryCode != nil {
			return factorConflict()
		}
		if f.Action != "Complete" || f.Credential == nil {
			return factorConflict()
		}
		var err error
		if f.GoogleContext != nil {
			plain, err := s.Crypto.Unprotect(*f.GoogleContext, "google-factor|"+f.User)
			if err != nil {
				return err
			}
			var flow googleContext
			if err = json.Unmarshal([]byte(plain), &flow); err != nil {
				return err
			}
			if err = s.finalizeGoogle(ctx, tx, f.User, flow, f.ID); err != nil {
				return err
			}
		}
		result, err = s.createSession(ctx, tx, f.User, f.Persistent, f.Credential)
		if err != nil {
			return err
		}
		if f.Previous != nil {
			if _, err = tx.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$3 WHERE id=$1 AND user_id=$2 AND revoked_at IS NULL`, f.Previous, f.User, s.Now().UTC()); err != nil {
				return err
			}
		}
		_, err = tx.Exec(ctx, `UPDATE two_factor_challenges SET consumed_at=$2,verified_at=$2,required_action='Complete' WHERE id=$1`, f.ID, s.Now().UTC())
		return err
	})
	return result, err
}

// TwoFactorStatus exposes no authenticator or recovery secrets.
func (s *Service) TwoFactorStatus(ctx context.Context, user string) (domain.TwoFactorStatusResponse, error) {
	var result domain.TwoFactorStatusResponse
	err := s.Store.Pool.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM member_two_factors WHERE member_id=$1 AND credential_id IS NOT NULL),(SELECT count(*) FROM two_factor_recovery_codes WHERE member_id=$1 AND consumed_at IS NULL AND (reserved_until IS NULL OR reserved_until<=$2))`, user, s.Now().UTC()).Scan(&result.IsEnabled, &result.RemainingRecoveryCodes)
	return result, err
}

// ReauthenticateTwoFactor binds a single management operation to its original live session.
func (s *Service) ReauthenticateTwoFactor(ctx context.Context, p Principal, input domain.TwoFactorReauthenticationRequest) (domain.TwoFactorChallengeResponse, error) {
	var result domain.TwoFactorChallengeResponse
	var rejected error
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		f, err := s.loadFactor(ctx, tx, p.MemberID)
		if err != nil {
			return err
		}
		if f.Credential == nil {
			return factorConflict()
		}
		if err = s.startFactorVerification(ctx, tx, &f); err != nil {
			rejected = err
			return nil
		}
		purpose := upperFirst(text(input.Purpose))
		if input.RecoveryCode != nil && purpose != "ReplaceAuthenticator" {
			return factorConflict()
		}
		if input.RecoveryCode == nil {
			if err = s.verifyCurrentFactor(ctx, tx, &f, text(input.Code)); err != nil {
				var business *domain.Error
				if errors.As(err, &business) {
					rejected = err
					return nil
				}
				return err
			}
		}
		result, err = s.beginFactor(ctx, tx, p.MemberID, purpose, false, &p.SessionID, nil)
		if err != nil {
			return err
		}
		if input.RecoveryCode != nil {
			if err = tx.QueryRow(ctx, `SELECT id::text,expires_at FROM two_factor_challenges WHERE flow_hash=$1`, security.HashToken(result.Flow)).Scan(&f.ID, &f.Expires); err != nil {
				return err
			}
			if err = s.reserveRecovery(ctx, tx, &f, *input.RecoveryCode); err != nil {
				var business *domain.Error
				if errors.As(err, &business) {
					rejected = err
					return nil
				}
				return err
			}
		}
		return nil
	})
	if err == nil {
		err = rejected
	}
	return result, err
}

// RegenerateRecoveryCodes consumes a dedicated management grant and revokes existing sessions.
func (s *Service) RegenerateRecoveryCodes(ctx context.Context, flow string, p Principal) (domain.TwoFactorRecoveryCodesResponse, error) {
	var result domain.TwoFactorRecoveryCodesResponse
	err := s.factorFlow(ctx, flow, p, func(tx pgx.Tx, f *factorState) error {
		if f.Purpose != "RegenerateRecoveryCodes" || f.Action != "Complete" || f.Credential == nil {
			return factorConflict()
		}
		var err error
		result.RecoveryCodes, err = s.recoveryCodes(ctx, tx, f)
		if err != nil {
			return err
		}
		if err = s.revokeFactorProofs(ctx, tx, f); err != nil {
			return err
		}
		if _, err = tx.Exec(ctx, `UPDATE two_factor_challenges SET consumed_at=$2 WHERE id=$1`, f.ID, s.Now().UTC()); err != nil {
			return err
		}
		return s.queueEmail(ctx, tx, f.User, "TWO_FACTOR_RECOVERY_CODES_REGENERATED", &f.Email, nil)
	})
	return result, err
}
