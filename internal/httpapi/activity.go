package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/validation"
)

type pageVisitRequest struct {
	Page string `json:"page"`
}

func (a *API) activityRoutes() {
	a.route("POST /api/v1/member-activity/page-visits", options{Auth: true, CSRF: true, Rate: 120, RateByMember: true}, a.pageVisit)
	a.route("GET /api/v1/admin/member-activity-events", options{Auth: true, Admin: true, Rate: 120}, a.activityEvents)
}

func (a *API) pageVisit(r *request) (response, error) {
	input, err := body[pageVisitRequest](r)
	if err != nil {
		return response{}, err
	}
	label, err := validation.ActivityPage(input.Page)
	if err != nil {
		return response{}, err
	}
	if err = a.Service.RecordActivity(r.Context(), r.Principal.MemberID, "pageVisit", label, "info", activityPayload(input), r.Principal.ActorMemberID); err != nil {
		return response{}, err
	}
	return response{Status: 204}, nil
}

func (a *API) activityEvents(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	query, kind, level, err := validation.ActivityQuery(r.URL.Query())
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.ActivityEvents(r.Context(), r.Principal, query, kind, level, page, size)
	return response{Body: result}, err
}
