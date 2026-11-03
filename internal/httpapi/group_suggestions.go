package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/images"
)

func (a *API) groupSuggestionRoutes() {
	base := "/api/v1/groups/{groupId}/wishlists/{wishlistId}/suggestions"
	a.route("GET "+base+"/{suggestionId}/image/content", options{Auth: true, ImageRead: true, Rate: 1200, RateByMember: true}, a.groupSuggestionImageContent)
	a.route("POST /api/v1/groups/{groupId}/wishlists/{wishlistId}/suggestion-images/resolve", options{Auth: true, CSRF: true, Rate: 10, RateByMember: true}, a.resolveGroupSuggestionImage)
	for _, method := range []string{"GET", "POST"} {
		a.route(method+" "+base, options{Auth: true, CSRF: method != "GET", Rate: 60, RateByMember: true}, a.groupSuggestions)
	}
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		a.route(method+" "+base+"/{suggestionId}", options{Auth: true, CSRF: method != "GET"}, a.groupSuggestion)
		rate := 10
		if method == "GET" {
			rate = 1200 // A collection can contain 1000 distinct images.
		}
		a.route(method+" "+base+"/{suggestionId}/image", options{Auth: true, CSRF: method != "GET", Rate: rate, RateByMember: true}, a.groupSuggestionImage)
		a.route(method+" "+base+"/{suggestionId}/reservations/current", options{Auth: true, CSRF: method != "GET"}, a.groupSuggestionReservation)
	}
}

func (a *API) groupSuggestions(r *request) (response, error) {
	member, group, wishlist := r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId")
	if r.Method == "GET" {
		result, err := a.Service.GroupSuggestions(r.Context(), member, group, wishlist)
		return response{Body: result}, err
	}
	input, err := body[domain.SaveGroupSuggestionRequest](r)
	if err != nil {
		return response{}, err
	}
	result, version, err := a.Service.CreateGroupSuggestion(r.Context(), member, group, wishlist, input)
	return response{Status: 201, Body: result, Version: &version, Location: r.URL.Path + "/" + result.Id}, err
}

func (a *API) groupSuggestion(r *request) (response, error) {
	member, group, wishlist, id := r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId"), r.PathValue("suggestionId")
	if r.Method == "GET" {
		result, version, err := a.Service.GroupSuggestion(r.Context(), member, group, wishlist, id)
		return response{Body: result, Version: &version}, err
	}
	version, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	var input domain.SaveGroupSuggestionRequest
	if r.Method == "PUT" {
		input, err = body[domain.SaveGroupSuggestionRequest](r)
		if err != nil {
			return response{}, err
		}
	}
	result, updated, err := a.Service.ChangeGroupSuggestion(r.Context(), member, group, wishlist, id, r.Method, input, *version)
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	return response{Body: result, Version: &updated}, err
}

func (a *API) groupSuggestionImage(r *request) (response, error) {
	member, group, wishlist, id := r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId"), r.PathValue("suggestionId")
	if r.Method == "GET" {
		content, err := a.Service.GroupSuggestionImage(r.Context(), member, group, wishlist, id)
		if err == nil && images.MediaType(content) != "image/webp" {
			normalized, normalizeErr := images.Process(r.Context(), content, 1600, false)
			content, err = normalized.Content, normalizeErr
		}
		return response{Body: struct {
			Content []byte `json:"content"`
		}{Content: content}}, err
	}
	version, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	var content []byte
	if r.Method == "PUT" {
		content, err = imageUpload(r)
		if err != nil {
			return response{}, err
		}
	}
	result, updated, err := a.Service.ChangeGroupSuggestionImage(r.Context(), member, group, wishlist, id, content, *version, r.Method == "DELETE")
	if r.Method == "DELETE" {
		return response{Status: 204, Version: &updated}, err
	}
	return response{Body: result, Version: &updated}, err
}

func (a *API) groupSuggestionReservation(r *request) (response, error) {
	var version *uint32
	var err error
	if r.Method != "GET" {
		version, err = entityTag(r, r.Method == "DELETE")
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
	result, err := a.Service.GroupSuggestionReservation(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId"), r.PathValue("suggestionId"), r.Method, quantity, version)
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	status, location := 200, ""
	if result.Created {
		status, location = 201, r.URL.Path
	}
	return response{Status: status, Body: result.Reservation, Version: &result.Version, Location: location}, err
}

func (a *API) groupSuggestionImageContent(r *request) (response, error) {
	content, err := a.Service.GroupSuggestionImage(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId"), r.PathValue("suggestionId"))
	if err == nil && r.URL.Query().Get("size") == "thumbnail" {
		content, _, err = a.thumbnails.Thumbnail(r.Context(), content)
	}
	return response{Body: struct {
		Content []byte `json:"content"`
	}{Content: content}}, err
}
