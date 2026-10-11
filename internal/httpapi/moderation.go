package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func (a *API) moderationRoutes() {
	admin := options{Auth: true, Admin: true, Rate: 120}
	a.route("GET /api/v1/admin/reported-wishlists", admin, a.reportedLists)
	a.route("GET /api/v1/admin/reported-wishlists/{wishlistId}", admin, a.reportedList)
	a.route("GET /api/v1/admin/reported-wishlists/{wishlistId}/reports", admin, a.reports)
	a.route("GET /api/v1/admin/reported-wishlists/{wishlistId}/wishes/{wishId}/image", admin, a.readWishImage)
	a.route("GET /api/v1/admin/wishlists/{wishlistId}/moderation", admin, a.moderation)
	a.route("PUT /api/v1/admin/wishlists/{wishlistId}/moderation", admin, a.moderation)
	a.route("GET /api/v1/admin/wishlists/{wishlistId}/moderation/events", admin, a.moderationEvents)
	a.route("GET /api/v1/admin/reported-wishlists/{wishlistId}/reports/{reportId}", admin, a.report)
	a.route("PUT /api/v1/admin/reported-wishlists/{wishlistId}/reports/{reportId}", admin, a.report)
	a.route("GET /api/v1/admin/reported-wishlists/{wishlistId}/reports/{reportId}/events", admin, a.reviewEvents)
}
func (a *API) reportedLists(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	reason, status, suspended, err := validation.ReportFilters(r.URL.Query())
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.ReportedWishlists(r.Context(), reason, status, suspended, page, size)
	return response{Body: result}, err
}
func (a *API) reportedList(r *request) (response, error) {
	result, err := a.Service.ReportedWishlist(r.Context(), r.PathValue("wishlistId"))
	return response{Body: result}, err
}
func (a *API) reports(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	reason, status, _, err := validation.ReportFilters(r.URL.Query())
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.Reports(r.Context(), r.PathValue("wishlistId"), reason, status, page, size)
	return response{Body: result}, err
}
func (a *API) report(r *request) (response, error) {
	list, id := r.PathValue("wishlistId"), r.PathValue("reportId")
	if r.Method == "GET" {
		result, version, err := a.Service.Report(r.Context(), list, id)
		return response{Body: result, Version: &version}, err
	}
	version, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	input, err := body[domain.UpdateWishlistReportReviewRequest](r)
	if err != nil {
		return response{}, err
	}
	result, next, err := a.Service.ReviewReport(r.Context(), r.Principal, list, id, input, *version)
	return response{Body: result, Version: &next}, err
}
func (a *API) reviewEvents(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.ReportReviewEvents(r.Context(), r.PathValue("wishlistId"), r.PathValue("reportId"), page, size)
	return response{Body: result}, err
}
func (a *API) moderation(r *request) (response, error) {
	list := r.PathValue("wishlistId")
	if r.Method == "GET" {
		result, version, err := a.Service.Moderation(r.Context(), list)
		return response{Body: result, Version: &version}, err
	}
	version, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	input, err := body[domain.UpdateWishlistModerationRequest](r)
	if err != nil {
		return response{}, err
	}
	result, next, err := a.Service.UpdateModeration(r.Context(), r.Principal, list, input, *version)
	return response{Body: result, Version: &next}, err
}
func (a *API) moderationEvents(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.ModerationEvents(r.Context(), r.PathValue("wishlistId"), page, size)
	return response{Body: result}, err
}
