package httpapi

import "github.com/JennGllg/mon-kado/internal/domain"

func (a *API) pushRoutes() {
	const preferences = "/api/v1/members/current/push-preferences"
	a.route("GET "+preferences, options{Auth: true}, func(r *request) (response, error) {
		value, version, err := a.Service.PushPreferences(r.Context(), r.Principal.MemberID)
		return response{Body: value, Version: &version}, err
	})
	a.route("PUT "+preferences, options{Auth: true, CSRF: true}, func(r *request) (response, error) {
		version, err := entityTag(r, true)
		if err != nil {
			return response{}, err
		}
		input, err := body[domain.UpdateNotificationPreferencesRequest](r)
		if err != nil {
			return response{}, err
		}
		value, saved, err := a.Service.SavePushPreferences(r.Context(), r.Principal.MemberID, input, *version)
		return response{Body: value, Version: &saved}, err
	})
	const path = "/api/v1/members/current/push-subscriptions"
	a.route("GET /api/v1/members/current/push-configuration", options{Auth: true}, func(r *request) (response, error) {
		cfg := a.Service.Config.PushOptions
		var key *string
		if cfg.Enabled {
			key = &cfg.PublicKey
		}
		return response{Body: domain.PushConfigurationResponse{Enabled: cfg.Enabled, PublicKey: key}}, nil
	})
	a.route("GET "+path, options{Auth: true}, func(r *request) (response, error) {
		v, e := a.Service.PushSubscriptions(r.Context(), r.Principal)
		return response{Body: v}, e
	})
	a.route("POST "+path, options{Auth: true, CSRF: true}, func(r *request) (response, error) {
		input, err := body[domain.CreatePushSubscriptionRequest](r)
		if err != nil {
			return response{}, err
		}
		v, err := a.Service.CreatePushSubscription(r.Context(), r.Principal, input)
		return response{Status: 201, Body: v, Location: path + "/" + v.Id}, err
	})
	a.route("PUT "+path+"/{subscriptionId}", options{Auth: true, CSRF: true}, func(r *request) (response, error) {
		version, err := entityTag(r, true)
		if err != nil {
			return response{}, err
		}
		input, err := body[domain.UpdatePushSubscriptionRequest](r)
		if err != nil {
			return response{}, err
		}
		v, saved, err := a.Service.UpdatePushSubscription(r.Context(), r.Principal, r.PathValue("subscriptionId"), input, *version)
		return response{Body: v, Version: &saved}, err
	})
	a.route("GET "+path+"/{subscriptionId}", options{Auth: true}, func(r *request) (response, error) {
		values, err := a.Service.PushSubscriptions(r.Context(), r.Principal)
		if err != nil {
			return response{}, err
		}
		for _, v := range values {
			if v.Id == r.PathValue("subscriptionId") {
				return response{Body: v}, nil
			}
		}
		return response{}, domain.Failure(404, domain.PushSubscriptionNotFound, "The requested resource was not found.")
	})
	a.route("DELETE "+path+"/{subscriptionId}", options{Auth: true, CSRF: true}, func(r *request) (response, error) {
		return response{Status: 204}, a.Service.DeletePushSubscription(r.Context(), r.Principal, r.PathValue("subscriptionId"))
	})
}
