// Package application coordinates business operations and their transactions.
package application

import (
	"context"
	"errors"
	"strings"
	"time"

	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/persistence"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
)

// Service owns the business services, clock and provider-independent cryptography.
type Service struct {
	Store  *persistence.Store
	Crypto *security.Cryptography
	Config config.Config
	Now    func() time.Time
}

// New composes the application dependencies without performing business I/O.
func New(store *persistence.Store, cfg config.Config, now func() time.Time) *Service {
	service := &Service{Store: store, Config: cfg, Now: now}
	service.Crypto = &security.Cryptography{SigningKey: cfg.SigningKey, EncryptionKey: cfg.EncryptionKey, Issuer: cfg.Issuer, Audience: cfg.Audience, Now: func() time.Time { return service.Now() }}
	return service
}

func text(value *string) string {
	if value == nil {
		return ""
	}
	return *value
}
func optional(value *string) *string {
	if value == nil {
		return nil
	}
	v := strings.TrimSpace(*value)
	if v == "" {
		return nil
	}
	return &v
}
func quantity(value *int) int {
	if value == nil {
		return 1
	}
	return *value
}
func upperFirst(value string) string {
	if value == "" {
		return value
	}
	return strings.ToUpper(value[:1]) + value[1:]
}
func lowerFirst(value string) string {
	if value == "" {
		return value
	}
	return strings.ToLower(value[:1]) + value[1:]
}
func missing(code string) error {
	return domain.Failure(404, code, "The requested resource was not found.")
}
func stale(code string) error {
	return domain.Failure(412, code, "The resource has changed. Retrieve its current version.")
}
func unauthorized() error {
	return domain.Failure(401, domain.AccountAuthenticationSessionInvalid, "The authentication session is invalid.")
}
func duplicate(err error, constraint string) bool {
	var e *pgconn.PgError
	return errors.As(err, &e) && e.Code == "23505" && e.ConstraintName == constraint
}

// Principal contains server-verified member and session identities, never client roles.
type Principal struct {
	MemberID            string
	SessionID           string
	Roles               []string
	TwoFactorVerifiedAt *time.Time
}

// Authenticate proves that the signed access token still belongs to a live session.
func (s *Service) Authenticate(ctx context.Context, token string) (Principal, error) {
	claims, err := s.Crypto.VerifyAccessToken(token)
	if err != nil {
		return Principal{}, unauthorized()
	}
	p := Principal{MemberID: claims.Subject, Roles: []string{}}
	err = s.Store.Pool.QueryRow(ctx, `SELECT session.id::text,CASE WHEN session.two_factor_credential_id=f.credential_id THEN session.two_factor_verified_at END FROM authentication_access_tokens token JOIN authentication_sessions session ON session.id=token.session_id JOIN users u ON u.id=session.user_id LEFT JOIN member_two_factors f ON f.member_id=u.id WHERE token.id=$1 AND session.user_id=$2 AND session.revoked_at IS NULL AND session.expires_at>$3 AND token.expires_at>$3 AND u.email_confirmed AND (session.two_factor_credential_id IS NULL OR session.two_factor_credential_id=f.credential_id)`, claims.ID, claims.Subject, s.Now().UTC()).Scan(&p.SessionID, &p.TwoFactorVerifiedAt)
	if errors.Is(err, pgx.ErrNoRows) {
		return p, unauthorized()
	}
	if err != nil {
		return p, err
	}
	rows, err := s.Store.Pool.Query(ctx, `SELECT r.name FROM roles r JOIN user_roles ur ON ur.role_id=r.id WHERE ur.user_id=$1 ORDER BY r.name`, p.MemberID)
	if err != nil {
		return p, err
	}
	defer rows.Close()
	for rows.Next() {
		var role string
		if err = rows.Scan(&role); err != nil {
			return p, err
		}
		p.Roles = append(p.Roles, role)
	}
	return p, rows.Err()
}

// AuthorizeAdministrator centralizes access to disclosed administrative resources.
func AuthorizeAdministrator(p Principal) error {
	for _, role := range p.Roles {
		if role == "Administrator" {
			if p.TwoFactorVerifiedAt == nil {
				return domain.Failure(403, domain.AccountTwoFactorAccessDenied, "Administrator access requires verified two-factor authentication.")
			}
			return nil
		}
	}
	return domain.Failure(403, domain.SecurityForbidden, "The caller cannot access this resource.")
}

// AuthorizeWishlist conceals foreign private resources and blocks mutations of suspended lists.
func (s *Service) AuthorizeWishlist(ctx context.Context, owner, wishlist string, mutation bool) error {
	var suspended bool
	err := s.Store.Pool.QueryRow(ctx, "SELECT is_suspended FROM wishlists WHERE id=$1 AND owner_id=$2", wishlist, owner).Scan(&suspended)
	if errors.Is(err, pgx.ErrNoRows) {
		return missing(domain.WishlistNotFound)
	}
	if err != nil {
		return err
	}
	if mutation && suspended {
		return domain.Failure(409, domain.WishlistSuspended, "The wishlist is suspended.")
	}
	return nil
}
