package httpapi

import "github.com/JennGllg/mon-kado/internal/domain"

func (a *API) notificationPreferenceRoutes() {
	const path = "/api/v1/members/current/notification-preferences"
	a.route("GET "+path, options{Auth: true}, func(r *request) (response, error) {
		value, version, err := a.Service.NotificationPreferences(r.Context(), r.Principal.MemberID)
		return response{Body: value, Version: &version}, err
	})
	a.route("PUT "+path, options{Auth: true, CSRF: true}, func(r *request) (response, error) {
		version, err := entityTag(r, true)
		if err != nil {
			return response{}, err
		}
		input, err := body[domain.UpdateNotificationPreferencesRequest](r)
		if err != nil {
			return response{}, err
		}
		value, savedVersion, err := a.Service.SaveNotificationPreferences(r.Context(), r.Principal.MemberID, input, *version)
		return response{Body: value, Version: &savedVersion}, err
	})
}
