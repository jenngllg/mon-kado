package application

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
	"strings"
	"time"
)

// GoogleIdentity contains a verified identity without external SDK types.
type GoogleIdentity struct {
	Subject      string `json:"subject"`
	Email        string `json:"email"`
	DisplayName  string `json:"displayName"`
	HostedDomain string `json:"hostedDomain"`
}

// GoogleClient exchanges a nonce-bound PKCE authorization code once.
type GoogleClient interface {
	// Exchange verifies the provider's signed identity against the nonce and PKCE proof.
	Exchange(context.Context, string, string, string, string) (GoogleIdentity, error)
}
type googleContext struct {
	Nonce, Verifier, Redirect string
	Persistent                bool
	Previous                  *string
	Identity                  *GoogleIdentity
	Expected, Stamp           *string
	Claim, ReplaceName        bool
}

// GoogleChallenge contains only browser redirect material.
type GoogleChallenge struct {
	State, Binding, Nonce, VerifierChallenge string
	Expires                                  time.Time
}

func googleFailure() error {
	return domain.Failure(401, domain.GoogleAuthenticationFailed, "Google authentication could not be completed.")
}

// StartGoogle retains encrypted PKCE material in a short-lived browser-bound flow.
func (s *Service) StartGoogle(ctx context.Context, redirect string, persistent bool, currentRefresh string) (GoogleChallenge, error) {
	now, id := s.Now().UTC(), security.NewID()
	result := GoogleChallenge{State: security.RandomToken(), Binding: security.RandomToken(), Nonce: security.RandomToken(), Expires: now.Add(5 * time.Minute)}
	verifier := security.RandomToken()
	result.VerifierChallenge = base64.RawURLEncoding.EncodeToString(security.HashToken(verifier))
	flow := googleContext{Nonce: result.Nonce, Verifier: verifier, Redirect: redirect, Persistent: persistent}
	if session, err := security.SessionID(currentRefresh); err == nil {
		var previous string
		err = s.Store.Pool.QueryRow(ctx, `SELECT id::text FROM authentication_sessions WHERE id=$1 AND refresh_token_hash=$2 AND revoked_at IS NULL AND expires_at>$3`, session, security.HashToken(currentRefresh), now).Scan(&previous)
		if err != nil && !errors.Is(err, pgx.ErrNoRows) {
			return result, err
		}
		if err == nil {
			flow.Previous = &previous
		}
	}
	data, err := json.Marshal(flow)
	if err != nil {
		return result, err
	}
	protected, err := s.Crypto.Protect(string(data), "google-flow|"+id)
	if err != nil {
		return result, err
	}
	_, err = s.Store.Pool.Exec(ctx, `INSERT INTO google_authentication_flows(id,state_hash,binding_hash,protected_context,created_at,expires_at) VALUES($1,$2,$3,$4,$5,$6)`, id, security.HashToken(result.State), security.HashToken(result.Binding), protected, now, result.Expires)
	return result, err
}

// GoogleCallback consumes state before exchanging the code and binds the identity to this browser.
func (s *Service) GoogleCallback(ctx context.Context, state, binding, code string, client GoogleClient) (string, error) {
	var id, protected string
	err := s.Store.Pool.QueryRow(ctx, `UPDATE google_authentication_flows SET callback_claimed_at=$3 WHERE state_hash=$1 AND binding_hash=$2 AND expires_at>$3 AND callback_claimed_at IS NULL AND consumed_at IS NULL RETURNING id::text,protected_context`, security.HashToken(state), security.HashToken(binding), s.Now().UTC()).Scan(&id, &protected)
	if errors.Is(err, pgx.ErrNoRows) {
		return "", googleFailure()
	}
	if err != nil {
		return "", err
	}
	plain, err := s.Crypto.Unprotect(protected, "google-flow|"+id)
	if err != nil {
		return "", err
	}
	var flow googleContext
	if err = json.Unmarshal([]byte(plain), &flow); err != nil {
		return "", err
	}
	identity, err := client.Exchange(ctx, code, flow.Verifier, flow.Nonce, flow.Redirect)
	if err != nil {
		return "", err
	}
	flow.Nonce, flow.Verifier = "", ""
	flow.Identity = &identity
	var target, stamp string
	err = s.Store.Pool.QueryRow(ctx, `SELECT u.id::text,u.security_stamp FROM users u LEFT JOIN user_logins l ON l.user_id=u.id AND l.login_provider='Google' AND l.provider_key=$1 WHERE l.user_id IS NOT NULL OR u.normalized_email=$2 ORDER BY (l.user_id IS NOT NULL) DESC LIMIT 1`, identity.Subject, strings.ToUpper(identity.Email)).Scan(&target, &stamp)
	if err != nil && !errors.Is(err, pgx.ErrNoRows) {
		return "", err
	}
	if err == nil {
		flow.Expected = &target
		flow.Stamp = &stamp
	}
	token := security.RandomToken()
	data, err := json.Marshal(flow)
	if err != nil {
		return "", err
	}
	protected, err = s.Crypto.Protect(string(data), "google-flow|"+id)
	if err != nil {
		return "", err
	}
	command, err := s.Store.Pool.Exec(ctx, `UPDATE google_authentication_flows SET completion_hash=$2,protected_context=$3 WHERE id=$1 AND consumed_at IS NULL AND expires_at>$4`, id, security.HashToken(token), protected, s.Now().UTC())
	if err == nil && command.RowsAffected() != 1 {
		err = googleFailure()
	}
	return token, err
}

// CompleteGoogle resolves or links an identity and defers administrative association until MFA succeeds.
func (s *Service) CompleteGoogle(ctx context.Context, proof, binding string, password *string) (SessionResult, error) {
	var result SessionResult
	var rejected error
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		var flowID, protected string
		err := tx.QueryRow(ctx, `SELECT id::text,protected_context FROM google_authentication_flows WHERE completion_hash=$1 AND binding_hash=$2 AND callback_claimed_at IS NOT NULL AND consumed_at IS NULL AND expires_at>$3 FOR UPDATE`, security.HashToken(proof), security.HashToken(binding), s.Now().UTC()).Scan(&flowID, &protected)
		if errors.Is(err, pgx.ErrNoRows) {
			return googleFailure()
		}
		if err != nil {
			return err
		}
		plain, err := s.Crypto.Unprotect(protected, "google-flow|"+flowID)
		if err != nil {
			return err
		}
		var flow googleContext
		if err = json.Unmarshal([]byte(plain), &flow); err != nil {
			return err
		}
		if flow.Identity == nil {
			return googleFailure()
		}
		identity := flow.Identity
		normalized := strings.ToUpper(identity.Email)
		var user, stamp, email string
		var hash *string
		var confirmed bool
		var locked *time.Time
		var failures int
		err = tx.QueryRow(ctx, `SELECT id::text,security_stamp,email,password_hash,email_confirmed,lockout_end,access_failed_count FROM users WHERE ($1::uuid IS NOT NULL AND id=$1) OR ($1::uuid IS NULL AND normalized_email=$2) FOR UPDATE`, flow.Expected, normalized).Scan(&user, &stamp, &email, &hash, &confirmed, &locked, &failures)
		fresh := errors.Is(err, pgx.ErrNoRows)
		if err != nil && !fresh {
			return err
		}
		if !fresh && (flow.Expected == nil || text(flow.Stamp) != stamp) || fresh && flow.Expected != nil {
			return googleFailure()
		}
		authoritative := strings.HasSuffix(strings.ToLower(identity.Email), "@gmail.com") || identity.HostedDomain != ""
		if fresh {
			if password != nil || !authoritative {
				security.HashPassword("unavailable Google claim")
				return domain.Failure(409, domain.GoogleAdditionalVerificationRequired, "Additional identity verification is required.")
			}
			user = security.NewID()
			name := identity.DisplayName
			if name == "" {
				name = "MonKado member"
			}
			_, err = tx.Exec(ctx, `INSERT INTO users(id,display_name,created_at,user_name,normalized_user_name,email,normalized_email,email_confirmed,security_stamp,concurrency_stamp,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) VALUES($1,$2,$3,$4,$5,$4,$5,true,$6,$7,false,false,true,0)`, user, name, s.Now().UTC(), identity.Email, normalized, security.RandomToken(), security.NewID())
			if err != nil {
				return err
			}
			if _, err = tx.Exec(ctx, `INSERT INTO user_roles(user_id,role_id) SELECT $1,id FROM roles WHERE normalized_name='MEMBER'`, user); err != nil {
				return err
			}
		} else {
			var linked, other bool
			err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM user_logins WHERE login_provider='Google' AND provider_key=$1 AND user_id=$2),EXISTS(SELECT 1 FROM user_logins WHERE login_provider='Google' AND (provider_key=$1 AND user_id<>$2 OR user_id=$2 AND provider_key<>$1))`, identity.Subject, user).Scan(&linked, &other)
			if err != nil {
				return err
			}
			if other {
				return domain.Failure(409, domain.GoogleAccountLinkConflict, "The Google account association conflicts with an existing identity.")
			}
			canConfirm := !confirmed && authoritative && strings.EqualFold(email, identity.Email)
			if locked != nil && locked.After(s.Now()) && !canConfirm {
				return googleFailure()
			}
			if password != nil {
				if !confirmed || hash == nil || !security.VerifyPassword(*password, *hash) {
					failures++
					var until *time.Time
					if failures >= 5 {
						at := s.Now().UTC().Add(5 * time.Minute)
						until = &at
						failures = 0
					}
					if _, err = tx.Exec(ctx, `UPDATE users SET access_failed_count=$2,lockout_end=$3 WHERE id=$1`, user, failures, until); err != nil {
						return err
					}
					rejected = domain.Failure(401, domain.GoogleAccountLinkFailed, "The local account proof is invalid.")
					return nil
				}
			} else if !linked {
				if !(strings.HasSuffix(strings.ToLower(identity.Email), "@gmail.com") || !confirmed && identity.HostedDomain != "") {
					if hash != nil && confirmed {
						return domain.Failure(409, domain.GoogleAccountLinkRequired, "Prove the current local password to link this Google account.")
					}
					security.HashPassword("unavailable Google claim")
					return domain.Failure(409, domain.GoogleAdditionalVerificationRequired, "Additional identity verification is required.")
				}
				flow.Claim = true
				flow.ReplaceName = !confirmed
			} else if !confirmed {
				if !canConfirm {
					return googleFailure()
				}
				flow.Claim = true
				flow.ReplaceName = true
			}
			var mfa bool
			if err = tx.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$1 AND r.normalized_name='ADMINISTRATOR') OR EXISTS(SELECT 1 FROM member_two_factors WHERE member_id=$1 AND credential_id IS NOT NULL)`, user).Scan(&mfa); err != nil {
				return err
			}
			if mfa {
				challenge, err := s.beginFactor(ctx, tx, user, "SignIn", flow.Persistent, nil, flow.Previous)
				if err != nil {
					return err
				}
				data, err := json.Marshal(flow)
				if err != nil {
					return err
				}
				protected, err := s.Crypto.Protect(string(data), "google-factor|"+user)
				if err != nil {
					return err
				}
				if _, err = tx.Exec(ctx, `UPDATE two_factor_challenges SET google_flow_id=$2,protected_google_context=$3 WHERE flow_hash=$1`, security.HashToken(challenge.Flow), flowID, protected); err != nil {
					return err
				}
				result.Challenge = &challenge
			}
		}
		if result.Challenge == nil {
			if err = s.finalizeGoogle(ctx, tx, user, flow, ""); err != nil {
				return err
			}
			result, err = s.createSession(ctx, tx, user, flow.Persistent, nil)
			if err != nil {
				return err
			}
			if flow.Previous != nil {
				if _, err = tx.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$2 WHERE id=$1`, flow.Previous, s.Now().UTC()); err != nil {
					return err
				}
			}
		}
		_, err = tx.Exec(ctx, `UPDATE google_authentication_flows SET consumed_at=$2 WHERE id=$1`, flowID, s.Now().UTC())
		return err
	})
	if err == nil {
		err = rejected
	}
	if duplicate(err, "pk_user_logins") || duplicate(err, "ux_user_logins_user_id_login_provider") {
		err = domain.Failure(409, domain.GoogleAccountLinkConflict, "The Google account association conflicts with an existing identity.")
	}
	return result, err
}

func (s *Service) finalizeGoogle(ctx context.Context, tx pgx.Tx, user string, flow googleContext, preserved string) error {
	if flow.Identity == nil {
		return googleFailure()
	}
	var associated string
	err := tx.QueryRow(ctx, `SELECT provider_key FROM user_logins WHERE login_provider='Google' AND user_id=$1`, user).Scan(&associated)
	if err != nil && !errors.Is(err, pgx.ErrNoRows) {
		return err
	}
	if err == nil && associated != flow.Identity.Subject {
		return domain.Failure(409, domain.GoogleAccountLinkConflict, "The Google account association conflicts with an existing identity.")
	}
	if _, err := tx.Exec(ctx, `INSERT INTO user_logins(login_provider,provider_key,provider_display_name,user_id) VALUES('Google',$1,'Google',$2) ON CONFLICT (login_provider,provider_key) DO NOTHING`, flow.Identity.Subject, user); err != nil {
		return err
	}
	var actual string
	if err := tx.QueryRow(ctx, `SELECT user_id::text FROM user_logins WHERE login_provider='Google' AND provider_key=$1`, flow.Identity.Subject).Scan(&actual); err != nil {
		return err
	}
	// Factor failures preserve attempt counters, so reject an association conflict before changing credentials.
	if actual != user {
		return domain.Failure(409, domain.GoogleAccountLinkConflict, "The Google account association conflicts with an existing identity.")
	}
	if flow.Claim {
		name := flow.Identity.DisplayName
		if name == "" {
			name = "MonKado member"
		}
		if _, err := tx.Exec(ctx, `UPDATE users SET password_hash=NULL,security_stamp=$2,email_confirmed=true,unconfirmed_account_expires_at=NULL,display_name=CASE WHEN $3 THEN $4 ELSE display_name END,updated_at=$5 WHERE id=$1`, user, security.RandomToken(), flow.ReplaceName, name, s.Now().UTC()); err != nil {
			return err
		}
		if _, err := tx.Exec(ctx, `UPDATE authentication_sessions SET revoked_at=$2 WHERE user_id=$1 AND revoked_at IS NULL`, user, s.Now().UTC()); err != nil {
			return err
		}
		if _, err := tx.Exec(ctx, `UPDATE member_email_change_requests SET revoked_at=$2 WHERE user_id=$1 AND confirmed_at IS NULL AND revoked_at IS NULL`, user, s.Now().UTC()); err != nil {
			return err
		}
		if _, err := tx.Exec(ctx, `UPDATE two_factor_challenges SET invalidated_at=$3 WHERE member_id=$1 AND ($2::uuid IS NULL OR id<>$2) AND consumed_at IS NULL AND invalidated_at IS NULL`, user, optionalString(preserved), s.Now().UTC()); err != nil {
			return err
		}
	}
	_, err = tx.Exec(ctx, `UPDATE users SET access_failed_count=0,lockout_end=NULL WHERE id=$1`, user)
	return err
}
func optionalString(value string) *string {
	if value == "" {
		return nil
	}
	return &value
}
