package httpapi

import (
	"context"
	"net/http"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func (a *API) routes() {
	a.registerCSRF()
	a.accountRoutes()
	a.subscriptionRoutes()
	a.copyRoutes()
	a.previewRoutes()
	a.sharingRoutes()
	a.imageRoutes()
	a.twoFactorRoutes()
	a.moderationRoutes()
	a.exportRoutes()
	a.administrationRoutes()
	a.googleRoutes()
	a.route("POST /api/v1/wishlists/{wishlistId}/wish-import-previews", options{Auth: true, Rate: a.Service.Config.Imports().PermitLimit, RateByMember: true, ImageAdmission: true}, a.importPreview)
	a.route("GET /liveness", options{}, func(r *request) (response, error) {
		return response{Binary: []byte("Healthy"), MediaType: "text/plain"}, nil
	})
	a.route("GET /readiness", options{}, func(r *request) (response, error) {
		ctx, cancel := context.WithTimeout(r.Context(), 2*time.Second)
		defer cancel()
		if err := a.Service.Store.Pool.Ping(ctx); err != nil {
			return response{}, domain.Failure(503, domain.TechnicalDependencyUnavailable, "PostgreSQL is unavailable.")
		}
		return response{Binary: []byte("Healthy"), MediaType: "text/plain"}, nil
	})
	a.route("POST /api/v1/auth/registrations", options{CSRF: true, Rate: 5}, a.register)
	a.route("POST /api/v1/auth/sessions", options{CSRF: true, Rate: 10}, a.login)
	a.route("POST /api/v1/auth/sessions/refresh", options{CSRF: true, Rate: 30}, a.refresh)
	a.route("DELETE /api/v1/auth/sessions/current", options{CSRF: true, Rate: 30}, a.logout)
	a.route("GET /api/v1/auth/sessions/current", options{Auth: true}, a.current)
	for _, method := range []string{"GET", "POST"} {
		a.route(method+" /api/v1/wishlists", options{Auth: true}, a.wishlists)
	}
	for _, method := range []string{"GET", "PUT", "PATCH", "DELETE"} {
		a.route(method+" /api/v1/wishlists/{wishlistId}", options{Auth: true}, a.wishlist)
	}
	for _, method := range []string{"GET", "POST", "PATCH"} {
		a.route(method+" /api/v1/wishlists/{wishlistId}/wishes", options{Auth: true}, a.wishes)
	}
	for _, method := range []string{"GET", "PUT", "PATCH", "DELETE"} {
		a.route(method+" /api/v1/wishlists/{wishlistId}/wishes/{wishId}", options{Auth: true}, a.wish)
	}
}

func (a *API) register(r *request) (response, error) {
	input, err := body[domain.RegisterAccountRequest](r)
	if err != nil {
		return response{}, err
	}
	return response{Status: 202}, a.Service.Register(r.Context(), input)
}
func (a *API) login(r *request) (response, error) {
	input, err := body[domain.LoginRequest](r)
	if err != nil {
		return response{}, err
	}
	session, err := a.Service.Login(r.Context(), input, a.cookie(r.Request, "Refresh"))
	if err != nil {
		return response{}, err
	}
	if session.Challenge != nil {
		return response{Status: 202, Body: session.Challenge}, nil
	}
	return response{Body: session.Access, Session: &session}, nil
}
func (a *API) refresh(r *request) (response, error) {
	session, err := a.Service.Refresh(r.Context(), a.cookie(r.Request, "Refresh"))
	return response{Body: session.Access, Session: &session}, err
}
func (a *API) logout(r *request) (response, error) {
	err := a.Service.Logout(r.Context(), a.cookie(r.Request, "Refresh"))
	return response{Status: 204, Session: &application.SessionResult{}}, err
}
func (a *API) current(r *request) (response, error) {
	result, v, err := a.Service.CurrentSession(r.Context(), r.Principal)
	return response{Body: result, Version: &v}, err
}
func (a *API) wishlists(r *request) (response, error) {
	if r.Method == http.MethodGet {
		archived, err := validation.QueryBoolean(r.URL.Query(), "isArchived")
		if err != nil {
			return response{}, err
		}
		result, err := a.Service.Wishlists(r.Context(), r.Principal.MemberID, archived)
		return response{Body: result}, err
	}
	input, err := body[domain.CreateWishlistRequest](r)
	if err != nil {
		return response{}, err
	}
	result, v, err := a.Service.CreateWishlist(r.Context(), r.Principal.MemberID, input)
	return response{Status: 201, Body: result, Version: &v, Location: "/api/v1/wishlists/" + result.Id}, err
}
func (a *API) wishlist(r *request) (response, error) {
	id, owner := r.PathValue("wishlistId"), r.Principal.MemberID
	if r.Method == http.MethodGet {
		result, v, err := a.Service.Wishlist(r.Context(), owner, id)
		return response{Body: result, Version: &v}, err
	}
	v, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	if r.Method == http.MethodDelete {
		return response{Status: 204}, a.Service.DeleteWishlist(r.Context(), owner, id, *v)
	}
	if r.Method == http.MethodPatch {
		input, err := body[domain.SetWishlistArchivedRequest](r)
		if err != nil {
			return response{}, err
		}
		result, version, err := a.Service.SetWishlistArchived(r.Context(), owner, id, *input.IsArchived, *v)
		return response{Body: result, Version: &version}, err
	}
	input, err := body[domain.UpdateWishlistRequest](r)
	if err != nil {
		return response{}, err
	}
	result, version, err := a.Service.UpdateWishlist(r.Context(), owner, id, input, *v)
	return response{Body: result, Version: &version}, err
}
func (a *API) wishes(r *request) (response, error) {
	id, owner := r.PathValue("wishlistId"), r.Principal.MemberID
	if r.Method == http.MethodGet {
		result, v, err := a.Service.Wishes(r.Context(), owner, id)
		return response{Body: result, Version: &v}, err
	}
	if r.Method == http.MethodPatch {
		v, err := entityTag(r, true)
		if err != nil {
			return response{}, err
		}
		input, err := body[domain.ReorderWishesRequest](r)
		if err != nil {
			return response{}, err
		}
		result, version, err := a.Service.ReorderWishes(r.Context(), owner, id, input.WishIds, *v)
		return response{Body: result, Version: &version}, err
	}
	input, err := body[domain.CreateWishRequest](r)
	if err != nil {
		return response{}, err
	}
	result, v, err := a.Service.CreateWish(r.Context(), owner, id, input)
	return response{Status: 201, Body: result, Version: &v, Location: "/api/v1/wishlists/" + id + "/wishes/" + result.Id}, err
}
func (a *API) wish(r *request) (response, error) {
	list, id, owner := r.PathValue("wishlistId"), r.PathValue("wishId"), r.Principal.MemberID
	if r.Method == http.MethodGet {
		result, v, err := a.Service.Wish(r.Context(), owner, list, id)
		return response{Body: result, Version: &v}, err
	}
	v, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	if r.Method == http.MethodDelete {
		return response{Status: 204}, a.Service.DeleteWish(r.Context(), owner, list, id, *v)
	}
	if r.Method == http.MethodPatch {
		input, err := body[domain.SetWishFavoriteRequest](r)
		if err != nil {
			return response{}, err
		}
		result, version, err := a.Service.SetWishFavorite(r.Context(), owner, list, id, *input.IsFavorite, *v)
		return response{Body: result, Version: &version}, err
	}
	input, err := body[domain.UpdateWishRequest](r)
	if err != nil {
		return response{}, err
	}
	result, version, err := a.Service.UpdateWish(r.Context(), owner, list, id, input, *v)
	return response{Body: result, Version: &version}, err
}
