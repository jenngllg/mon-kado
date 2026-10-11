package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func (a *API) administrationRoutes() {
	a.route("POST /api/v1/admin/members/{memberId}/erasure-requests", options{Auth: true, Admin: true, Rate: 5}, a.eraseMember)
	a.route("POST /api/v1/admin/members/{memberId}/session-revocations", options{Auth: true, Admin: true, Rate: 10}, a.revokeMemberSessions)
	a.route("GET /api/v1/admin/audit-events", options{Auth: true, Admin: true, Rate: 120}, a.auditEvents)
}
func (a *API) eraseMember(r *request) (response, error) {
	input, err := body[domain.ExecuteAdministrativeAccountErasureRequest](r)
	if err != nil {
		return response{}, err
	}
	if err = validation.ConfirmedMember(input.ConfirmedMemberId, r.PathValue("memberId")); err != nil {
		return response{}, err
	}
	return response{Status: 204}, a.Service.AdministrativeMemberAction(r.Context(), r.Principal, r.PathValue("memberId"), *input.RequestReference, true)
}
func (a *API) revokeMemberSessions(r *request) (response, error) {
	input, err := body[domain.RevokeMemberSessionsRequest](r)
	if err != nil {
		return response{}, err
	}
	if err = validation.ConfirmedMember(input.ConfirmedMemberId, r.PathValue("memberId")); err != nil {
		return response{}, err
	}
	return response{Status: 204}, a.Service.AdministrativeMemberAction(r.Context(), r.Principal, r.PathValue("memberId"), *input.RequestReference, false)
}
func (a *API) auditEvents(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	filter, err := validation.AuditQuery(r.URL.Query())
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.AuditEvents(r.Context(), filter, page, size)
	return response{Body: result}, err
}
