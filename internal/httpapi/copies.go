package httpapi

import "github.com/JennGllg/mon-kado/internal/domain"

func (a *API) copyRoutes() {
	a.route("POST /api/v1/wishlists/{wishlistId}/wishes/{wishId}/copies", options{Auth: true, CSRF: true, Rate: 10, ImageAdmission: true, RateByMember: true, RatePolicy: "giftImageUpload"}, a.copyWish)
	a.route("POST /api/v1/wishlists/{wishlistId}/wishes/copies", options{Auth: true, CSRF: true, Rate: 10, ImageAdmission: true, RateByMember: true, RatePolicy: "giftImageUpload"}, a.copyWish)
}

func (a *API) copyWish(r *request) (response, error) {
	source, destination, wish, link := r.PathValue("wishlistId"), "", r.PathValue("wishId"), ""
	if wish != "" {
		input, err := body[domain.CopyOwnedWishRequest](r)
		if err != nil {
			return response{}, err
		}
		destination = *input.DestinationWishlistId
	} else {
		input, err := body[domain.CopyWishRequest](r)
		if err != nil {
			return response{}, err
		}
		destination, source, wish, link = source, "", *input.SourceWishId, *input.SourceShareLinkId
	}
	result, version, err := a.Service.CopyWish(r.Context(), r.Principal.MemberID, destination, source, wish, link, r.Header.Get("X-MonKado-Share-Token"))
	return response{Status: 201, Body: result, Version: &version, Location: "/api/v1/wishlists/" + destination + "/wishes/" + result.Id}, err
}
