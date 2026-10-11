package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
	"strings"
	"unicode/utf8"
)

func (a *API) accountRoutes() {
	a.route("GET /api/v1/members", options{Rate: 60}, func(r *request) (response, error) {
		page, size, err := pagination(r)
		if err != nil {
			return response{}, err
		}
		name := strings.TrimSpace(r.URL.Query().Get("displayName"))
		if utf8.RuneCountInString(name) < 2 || !validation.Text(name, 80, false) {
			return response{}, &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The search term is invalid.", Validation: []domain.ValidationError{{PropertyName: "displayName", ErrorMessage: "The display name search must contain between 2 and 80 characters."}}}
		}
		result, err := a.Service.SearchMembers(r.Context(), name, page, size)
		return response{Body: result}, err
	})
	a.route("PUT /api/v1/members/current/email", options{Auth: true, Rate: 5}, func(r *request) (response, error) {
		version, err := entityTag(r, true)
		if err != nil {
			return response{}, err
		}
		input, err := body[domain.UpdateMemberEmailRequest](r)
		if err != nil {
			return response{}, err
		}
		return response{Status: 202}, a.Service.RequestEmailChange(r.Context(), r.Principal.MemberID, input, *version)
	})
	a.route("POST /api/v1/auth/email-change-confirmations", options{CSRF: true, Rate: 10}, func(r *request) (response, error) {
		input, err := body[domain.ConfirmMemberEmailChangeRequest](r)
		if err != nil {
			return response{}, err
		}
		return response{Status: 204}, a.Service.ConfirmEmailChange(r.Context(), input)
	})
	a.route("POST /api/v1/members/current/deletion-requests", options{Auth: true, Rate: 5}, func(r *request) (response, error) {
		return response{Status: 202}, a.Service.RequestDeletion(r.Context(), r.Principal.MemberID)
	})
	a.route("POST /api/v1/members/current/deletion-requests/confirm", options{Auth: true, Rate: 5}, func(r *request) (response, error) {
		input, err := body[domain.ConfirmMemberAccountDeletionRequest](r)
		if err != nil {
			return response{}, err
		}
		return response{Status: 204, Session: &application.SessionResult{}}, a.Service.ConfirmDeletion(r.Context(), r.Principal.MemberID, input)
	})
	a.route("POST /api/v1/auth/email-confirmations", options{CSRF: true, Rate: 10}, func(r *request) (response, error) {
		input, err := body[domain.ConfirmEmailRequest](r)
		if err != nil {
			return response{}, err
		}
		return response{Status: 204}, a.Service.ConfirmEmail(r.Context(), input)
	})
	a.route("POST /api/v1/auth/email-confirmation-requests", options{CSRF: true, Rate: 5}, func(r *request) (response, error) {
		input, err := body[domain.RequestEmailConfirmationRequest](r)
		if err != nil {
			return response{}, err
		}
		return response{Status: 202}, a.Service.RequestAccountEmail(r.Context(), *input.Email, false)
	})
	a.route("POST /api/v1/auth/password-reset-requests", options{CSRF: true, Rate: 5}, func(r *request) (response, error) {
		input, err := body[domain.RequestPasswordResetRequest](r)
		if err != nil {
			return response{}, err
		}
		return response{Status: 202}, a.Service.RequestAccountEmail(r.Context(), *input.Email, true)
	})
	a.route("POST /api/v1/auth/password-resets", options{CSRF: true, Rate: 5}, func(r *request) (response, error) {
		input, err := body[domain.ResetPasswordRequest](r)
		if err != nil {
			return response{}, err
		}
		return response{Status: 204}, a.Service.ResetPassword(r.Context(), input)
	})
	a.route("PUT /api/v1/members/current/profile", options{Auth: true}, func(r *request) (response, error) {
		v, err := entityTag(r, true)
		if err != nil {
			return response{}, err
		}
		input, err := body[domain.UpdateMemberProfileRequest](r)
		if err != nil {
			return response{}, err
		}
		result, version, err := a.Service.UpdateProfile(r.Context(), r.Principal.MemberID, input, *v)
		return response{Body: result, Version: &version}, err
	})
	a.route("PUT /api/v1/members/current/password", options{Auth: true}, func(r *request) (response, error) {
		input, err := body[domain.UpdateMemberPasswordRequest](r)
		if err != nil {
			return response{}, err
		}
		return response{Status: 204, Session: &application.SessionResult{}}, a.Service.UpdatePassword(r.Context(), r.Principal.MemberID, input)
	})
}
