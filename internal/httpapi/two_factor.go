package httpapi

import "github.com/JennGllg/mon-kado/internal/domain"

func (a *API) twoFactorRoutes() {
	a.route("POST /api/v1/auth/two-factor/completions", options{CSRF: true, Rate: 30}, a.completeFactor)
	a.route("POST /api/v1/auth/two-factor/setup", options{CSRF: true, Rate: 30}, a.setupFactor)
	a.route("POST /api/v1/auth/two-factor/setup/confirmations", options{CSRF: true, Rate: 30}, a.confirmFactor)
	a.route("POST /api/v1/auth/two-factor/recovery-codes/regenerations", options{Auth: true, Rate: 10}, a.regenerateCodes)
	a.route("GET /api/v1/members/current/two-factor", options{Auth: true}, a.factorStatus)
	a.route("POST /api/v1/members/current/two-factor/reauthentications", options{Auth: true, Rate: 10}, a.reauthenticateFactor)
}

func (a *API) completeFactor(r *request) (response, error) {
	input, err := body[domain.TwoFactorCompletionRequest](r)
	if err != nil {
		return response{}, err
	}
	session, err := a.Service.CompleteTwoFactor(r.Context(), input, r.Principal)
	if session.Challenge != nil {
		return response{Status: 202, Body: session.Challenge}, err
	}
	return response{Body: session.Access, Session: &session}, err
}
func (a *API) setupFactor(r *request) (response, error) {
	input, err := body[domain.TwoFactorFlowRequest](r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.TwoFactorSetup(r.Context(), *input.Flow, r.Principal)
	return response{Body: result}, err
}
func (a *API) confirmFactor(r *request) (response, error) {
	input, err := body[domain.TwoFactorSetupConfirmationRequest](r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.ConfirmTwoFactorSetup(r.Context(), *input.Flow, *input.Code, r.Principal)
	return response{Body: result}, err
}
func (a *API) regenerateCodes(r *request) (response, error) {
	input, err := body[domain.TwoFactorFlowRequest](r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.RegenerateRecoveryCodes(r.Context(), *input.Flow, r.Principal)
	return response{Body: result}, err
}
func (a *API) factorStatus(r *request) (response, error) {
	result, err := a.Service.TwoFactorStatus(r.Context(), r.Principal.MemberID)
	return response{Body: result}, err
}
func (a *API) reauthenticateFactor(r *request) (response, error) {
	input, err := body[domain.TwoFactorReauthenticationRequest](r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.ReauthenticateTwoFactor(r.Context(), r.Principal, input)
	return response{Body: result}, err
}
