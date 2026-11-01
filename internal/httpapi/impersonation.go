package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/google/uuid"
	"strings"
)

func (a *API) impersonationRoutes() {
	a.route("GET /api/v1/admin/impersonations/{impersonationId}", options{Auth: true, Admin: true, Rate: 60}, func(r *request) (response, error) {
		principal, err := a.Service.ResolveImpersonation(r.Context(), r.Principal, r.PathValue("impersonationId"))
		if err != nil {
			return response{}, err
		}
		user, _, err := a.Service.CurrentSession(r.Context(), principal)
		return response{Body: application.ImpersonationResponse{ID: r.PathValue("impersonationId"), User: user}}, err
	})
	a.route("POST /api/v1/admin/members/{memberId}/impersonations", options{Auth: true, Admin: true, CSRF: true, Rate: 10}, func(r *request) (response, error) {
		result, err := a.Service.StartImpersonation(r.Context(), r.Principal, r.PathValue("memberId"))
		return response{Status: 201, Location: "/api/v1/admin/impersonations/" + result.ID, Body: result}, err
	})
	a.route("DELETE /api/v1/admin/impersonations/{impersonationId}", options{Auth: true, Admin: true, CSRF: true, Rate: 30}, func(r *request) (response, error) {
		return response{Status: 204}, a.Service.StopImpersonation(r.Context(), r.Principal, r.PathValue("impersonationId"))
	})
}

func (a *API) applyImpersonation(r *request, pattern string) error {
	id := r.Header.Get("X-MonKado-Impersonation")
	if id == "" {
		return nil
	}
	if _, err := uuid.Parse(id); err != nil {
		return domain.Failure(403, domain.AccountImpersonationDenied, "The impersonation identifier is invalid.")
	}
	p, err := a.Service.ResolveImpersonation(r.Context(), r.Principal, id)
	if err != nil {
		return err
	}
	r.Principal = p
	path := r.URL.Path
	for _, sensitive := range []string{"/data-exports", "/two-factor"} {
		if strings.Contains(path, sensitive) {
			return domain.Failure(403, domain.AccountImpersonationRestricted, "Account security and data exports are unavailable during impersonation.")
		}
	}
	business := strings.HasPrefix(path, "/api/v1/wishlists") || strings.HasPrefix(path, "/api/v1/groups") || strings.HasPrefix(path, "/api/v1/shared-wishlists") || strings.HasPrefix(path, "/api/v1/wishlist-subscriptions") || strings.HasPrefix(path, "/api/v1/group-invitations") || path == "/api/v1/members/current/profile" || path == "/api/v1/members/current/profile/image"
	allowedRead := r.Method == "GET" && (path == "/api/v1/auth/sessions/current" || path == "/api/v1/members/current/reservations" || (strings.HasPrefix(path, "/api/v1/members/") && !strings.HasPrefix(path, "/api/v1/members/current")))
	if business || allowedRead || pattern == "POST /api/v1/member-activity/page-visits" {
		return nil
	}
	return domain.Failure(403, domain.AccountImpersonationRestricted, "This operation is unavailable during impersonation.")
}

func (a *API) recordImpersonatedWriteAttempt(r *request, pattern string) error {
	if r.Principal.ActorMemberID == "" || r.Method == "GET" || r.Method == "HEAD" || pattern == "POST /api/v1/member-activity/page-visits" {
		return nil
	}
	if !a.validCSRF(r.Request, r.Principal.ActorMemberID) {
		return domain.Failure(400, domain.SecurityCSRFValidationFailed, "The CSRF token is invalid.")
	}
	// A delegated mutation must leave durable actor attribution before its handler runs.
	if err := a.Service.RecordActivity(r.Context(), r.Principal.MemberID, "action", "Tentative · "+activityLabel(pattern), "info", nil, r.Principal.ActorMemberID); err != nil {
		return domain.Failure(503, domain.TechnicalDependencyUnavailable, "The action cannot be audited at this time.")
	}
	return nil
}
