package httpapi

func (a *API) subscriptionRoutes() {
	a.route("POST /api/v1/shared-wishlists/{shareLinkId}/subscriptions", options{Auth: true, CSRF: true, Rate: 20}, func(r *request) (response, error) {
		result, err := a.Service.CreateSubscription(r.Context(), r.Principal.MemberID, r.PathValue("shareLinkId"), r.Header.Get(shareTokenHeader))
		return response{Status: 201, Body: result, Location: "/api/v1/wishlist-subscriptions/" + result.Id}, err
	})
	a.route("GET /api/v1/shared-wishlists/{shareLinkId}/subscriptions/current", options{Auth: true, Rate: 120}, func(r *request) (response, error) {
		result, err := a.Service.CurrentSubscription(r.Context(), r.Principal.MemberID, r.PathValue("shareLinkId"), r.Header.Get(shareTokenHeader))
		return response{Body: result}, err
	})
	a.route("GET /api/v1/wishlist-subscriptions", options{Auth: true}, func(r *request) (response, error) {
		page, size, err := pagination(r)
		if err != nil {
			return response{}, err
		}
		result, err := a.Service.Subscriptions(r.Context(), r.Principal.MemberID, page, size)
		return response{Body: result}, err
	})
	a.route("GET /api/v1/wishlist-subscriptions/{id}", options{Auth: true}, func(r *request) (response, error) {
		result, err := a.Service.Subscription(r.Context(), r.Principal.MemberID, r.PathValue("id"))
		return response{Body: result}, err
	})
	a.route("DELETE /api/v1/wishlist-subscriptions/{id}", options{Auth: true}, func(r *request) (response, error) {
		return response{Status: 204}, a.Service.DeleteSubscription(r.Context(), r.Principal.MemberID, r.PathValue("id"))
	})
}
