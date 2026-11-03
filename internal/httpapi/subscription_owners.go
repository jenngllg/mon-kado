package httpapi

func (a *API) subscriptionOwnerRoutes() {
	a.route("GET /api/v1/members/current/wishlist-subscription-owners", options{Auth: true}, func(r *request) (response, error) {
		page, size, err := pagination(r)
		if err != nil {
			return response{}, err
		}
		result, err := a.Service.SubscriptionOwners(r.Context(), r.Principal.MemberID, page, size)
		return response{Body: result}, err
	})
}
