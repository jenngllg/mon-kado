package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
	"time"
)

const shareTokenHeader = "X-MonKado-Share-Token"

func (a *API) sharingRoutes() {
	for _, method := range []string{"GET", "POST", "PUT", "DELETE"} {
		a.route(method+" /api/v1/wishlists/{wishlistId}/share-link", options{Auth: true}, a.shareLink)
	}
	a.route("GET /api/v1/shared-wishlists/{shareLinkId}", options{Rate: 120}, a.sharedWishlist)
	a.route("GET /api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}", options{Rate: 120}, a.sharedWish)
	a.route("POST /api/v1/shared-wishlists/{shareLinkId}/participants", options{CSRF: true, Rate: 10, RateByShare: true, RatePolicy: "sharedJoin"}, a.joinWishlist)
	a.route("GET /api/v1/shared-wishlists/{shareLinkId}/participants/current", options{Rate: 120}, a.currentParticipant)
	a.route("POST /api/v1/shared-wishlists/{shareLinkId}/reports", options{CSRF: true, Rate: 5, RateWindow: time.Hour, RateByShare: true, RatePolicy: "sharedReport"}, a.reportWishlist)
	a.route("GET /api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/reservations/current", options{Rate: 120}, a.reservation)
	a.route("GET /api/v1/wishlists/{wishlistId}/wishes/{wishId}/reservations/current", options{Auth: true}, a.reservation)
	for _, method := range []string{"PUT", "DELETE"} {
		a.route(method+" /api/v1/shared-wishlists/{shareLinkId}/wishes/{wishId}/reservations/current", options{CSRF: true, Rate: 20, RateByShare: true, RatePolicy: "sharedReservation"}, a.reservation)
		a.route(method+" /api/v1/wishlists/{wishlistId}/wishes/{wishId}/reservations/current", options{Auth: true, CSRF: true, Rate: 20, RatePolicy: "ownedReservation"}, a.reservation)
	}
	a.route("GET /api/v1/members/current/reservations", options{Auth: true}, a.reservationHistory)
}

func (a *API) shareLink(r *request) (response, error) {
	var version *uint32
	var err error
	if r.Method == "PUT" || r.Method == "DELETE" {
		version, err = entityTag(r, true)
		if err != nil {
			return response{}, err
		}
	}
	id := r.PathValue("wishlistId")
	result, v, err := a.Service.ShareLink(r.Context(), r.Principal.MemberID, id, r.Method, version)
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	status, location := 200, ""
	if r.Method == "POST" {
		status, location = 201, "/api/v1/wishlists/"+id+"/share-link"
	}
	return response{Status: status, Body: result, Version: &v, Location: location}, err
}

func (a *API) sharedWishlist(r *request) (response, error) {
	available, err := validation.QueryBoolean(r.URL.Query(), "availableOnly")
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.SharedWishlist(r.Context(), r.PathValue("shareLinkId"), r.Header.Get(shareTokenHeader), r.Principal.MemberID, a.cookie(r.Request, "Guest"), available)
	return response{Body: result}, err
}

func (a *API) sharedWish(r *request) (response, error) {
	result, err := a.Service.SharedWish(r.Context(), r.PathValue("shareLinkId"), r.Header.Get(shareTokenHeader), r.PathValue("wishId"), r.Principal.MemberID, a.cookie(r.Request, "Guest"))
	return response{Body: result}, err
}

func (a *API) joinWishlist(r *request) (response, error) {
	input := domain.JoinSharedWishlistRequest{}
	var err error
	if r.ContentLength != 0 {
		input, err = body[domain.JoinSharedWishlistRequest](r)
		if err != nil {
			return response{}, err
		}
	}
	if r.Principal.MemberID == "" && (input.DisplayName == nil || !validation.Text(*input.DisplayName, 80, false) || *input.DisplayName == "") {
		return response{}, &domain.Error{Status: 400, Code: domain.RequestValidationError, Message: "The display name is required for guests.", Validation: []domain.ValidationError{{PropertyName: "displayName", ErrorMessage: "The display name is mandatory."}}}
	}
	link := r.PathValue("shareLinkId")
	result, err := a.Service.JoinWishlist(r.Context(), link, r.Header.Get(shareTokenHeader), r.Principal.MemberID, a.cookie(r.Request, "Guest"), input)
	status, location := 200, ""
	if result.Created {
		status, location = 201, "/api/v1/shared-wishlists/"+link+"/participants/current"
	}
	return response{Status: status, Body: result.Participant, Location: location, GuestToken: result.GuestToken, GuestExpires: result.GuestExpires}, err
}

func (a *API) currentParticipant(r *request) (response, error) {
	result, err := a.Service.CurrentParticipant(r.Context(), r.PathValue("shareLinkId"), r.Header.Get(shareTokenHeader), r.Principal.MemberID, a.cookie(r.Request, "Guest"))
	return response{Body: result}, err
}

func (a *API) reportWishlist(r *request) (response, error) {
	input, err := body[domain.ReportSharedWishlistRequest](r)
	if err != nil {
		return response{}, err
	}
	return response{Status: 204}, a.Service.ReportWishlist(r.Context(), r.PathValue("shareLinkId"), r.Header.Get(shareTokenHeader), input)
}

func (a *API) reservation(r *request) (response, error) {
	var v *uint32
	var err error
	if r.Method != "GET" {
		v, err = entityTag(r, r.Method == "DELETE")
		if err != nil {
			return response{}, err
		}
	}
	quantity := 0
	if r.Method == "PUT" {
		input, err := body[domain.UpsertGiftReservationRequest](r)
		if err != nil {
			return response{}, err
		}
		quantity = *input.Quantity
	}
	var result application.ReservationResult
	if wishlist := r.PathValue("wishlistId"); wishlist != "" {
		result, err = a.Service.OwnedReservation(r.Context(), r.Principal.MemberID, wishlist, r.PathValue("wishId"), r.Method, quantity, v)
	} else {
		result, err = a.Service.Reservation(r.Context(), r.PathValue("shareLinkId"), r.Header.Get(shareTokenHeader), r.PathValue("wishId"), r.Principal.MemberID, a.cookie(r.Request, "Guest"), r.Method, quantity, v)
	}
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	status, location := 200, ""
	if result.Created {
		status, location = 201, r.URL.Path
	}
	return response{Status: status, Body: result.Reservation, Version: &result.Version, Location: location}, err
}

func (a *API) reservationHistory(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	status, err := validation.ReservationStatus(r.URL.Query())
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.ReservationHistory(r.Context(), r.Principal.MemberID, status, page, size)
	return response{Body: result}, err
}
