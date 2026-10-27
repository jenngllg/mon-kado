package httpapi

import (
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/validation"
)

func (a *API) groupRoutes() {
	a.route("GET /api/v1/wishlists/{wishlistId}/group-visibility", options{Auth: true}, a.wishlistGroupVisibility)
	a.route("PUT /api/v1/wishlists/{wishlistId}/group-visibility", options{Auth: true, CSRF: true}, a.wishlistGroupVisibility)
	a.route("GET /api/v1/groups/join", options{Rate: 30}, a.previewGroupCode)
	a.route("GET /api/v1/groups/{groupId}/preview", options{Rate: 60}, a.previewGroupInvitation)
	a.route("GET /api/v1/groups/{groupId}/preview/image", options{Rate: 120}, a.previewGroupInvitationImage)
	a.route("POST /api/v1/groups/join", options{Auth: true, CSRF: true, Rate: 10, RateByMember: true}, a.joinGroup)
	for _, method := range []string{"GET", "POST", "DELETE"} {
		a.route(method+" /api/v1/groups/{groupId}/join-link", options{Auth: true, CSRF: method != "GET", Rate: 20, RateByMember: true}, a.groupJoinLink)
	}
	for _, method := range []string{"GET", "POST"} {
		a.route(method+" /api/v1/groups", options{Auth: true, CSRF: method != "GET", Rate: 60, RateByMember: true}, a.groups)
	}
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		a.route(method+" /api/v1/groups/{groupId}", options{Auth: true, CSRF: method != "GET"}, a.group)
	}
	a.route("GET /api/v1/groups/{groupId}/members", options{Auth: true}, a.groupMembers)
	a.route("GET /api/v1/groups/{groupId}/image", options{Rate: 120}, a.readGroupProfileImage)
	for _, method := range []string{"PUT", "DELETE"} {
		a.route(method+" /api/v1/groups/{groupId}/image", options{Auth: true, CSRF: true, Rate: 10, RateByMember: true}, a.groupProfileImage)
	}
	a.route("DELETE /api/v1/groups/{groupId}/members/current", options{Auth: true, CSRF: true}, a.leaveGroup)
	a.route("DELETE /api/v1/groups/{groupId}/members/{memberId}", options{Auth: true, CSRF: true}, a.removeGroupMember)
	for _, method := range []string{"GET", "POST"} {
		a.route(method+" /api/v1/groups/{groupId}/invitations", options{Auth: true, CSRF: method != "GET", Rate: 30, RateByMember: true}, a.groupInvitations)
	}
	a.route("GET /api/v1/groups/{groupId}/invitations/{invitationId}", options{Auth: true}, a.groupInvitation)
	a.route("DELETE /api/v1/groups/{groupId}/invitations/{invitationId}", options{Auth: true, CSRF: true}, a.revokeGroupInvitation)
	a.route("GET /api/v1/group-invitations", options{Auth: true}, a.receivedGroupInvitations)
	a.route("GET /api/v1/group-invitations/{invitationId}", options{Auth: true}, a.receivedGroupInvitation)
	a.route("POST /api/v1/group-invitations/{invitationId}/accept", options{Auth: true, CSRF: true, Rate: 30, RateByMember: true}, a.answerGroupInvitation)
	a.route("DELETE /api/v1/group-invitations/{invitationId}", options{Auth: true, CSRF: true}, a.answerGroupInvitation)
	a.route("GET /api/v1/groups/{groupId}/wishlists", options{Auth: true}, a.groupWishlists)
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		a.route(method+" /api/v1/groups/{groupId}/wishlists/{wishlistId}", options{Auth: true, CSRF: method != "GET"}, a.groupWishlist)
	}
	a.route("GET /api/v1/groups/{groupId}/wishlists/{wishlistId}/wishes/{wishId}", options{Auth: true}, a.groupWish)
	for _, method := range []string{"GET", "PUT", "DELETE"} {
		a.route(method+" /api/v1/groups/{groupId}/wishlists/{wishlistId}/wishes/{wishId}/reservations/current", options{Auth: true, CSRF: method != "GET"}, a.groupReservation)
	}
	a.route("GET /api/v1/groups/{groupId}/wishlists/{wishlistId}/wishes/{wishId}/image", options{}, a.groupWishImage)
}

func (a *API) groupJoinLink(r *request) (response, error) {
	var expected *uint32
	var err error
	if r.Method == "DELETE" {
		expected, err = entityTag(r, true)
		if err != nil {
			return response{}, err
		}
	}
	result, version, err := a.Service.GroupJoinLink(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.Method, expected)
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	status, location := 200, ""
	if r.Method == "POST" {
		status, location = 201, r.URL.Path
	}
	return response{Status: status, Body: result, Version: &version, Location: location}, err
}

func (a *API) joinGroup(r *request) (response, error) {
	code, err := validation.GroupJoinCode(r.Header.Get(shareTokenHeader))
	if err != nil {
		return response{}, err
	}
	result, created, err := a.Service.JoinGroup(r.Context(), r.Principal.MemberID, code)
	status, location := 200, ""
	if created {
		status, location = 201, "/api/v1/groups/"+result.Id
	}
	return response{Status: status, Body: result, Location: location}, err
}

func (a *API) groups(r *request) (response, error) {
	if r.Method == "GET" {
		page, size, err := pagination(r)
		if err != nil {
			return response{}, err
		}
		result, err := a.Service.Groups(r.Context(), r.Principal.MemberID, page, size)
		return response{Body: result}, err
	}
	input, err := body[domain.SaveGroupRequest](r)
	if err != nil {
		return response{}, err
	}
	result, v, err := a.Service.CreateGroup(r.Context(), r.Principal.MemberID, input)
	return response{Status: 201, Body: result, Version: &v, Location: "/api/v1/groups/" + result.Id}, err
}

func (a *API) group(r *request) (response, error) {
	var v *uint32
	var err error
	var input domain.SaveGroupRequest
	if r.Method != "GET" {
		v, err = entityTag(r, true)
		if err != nil {
			return response{}, err
		}
	}
	if r.Method == "PUT" {
		input, err = body[domain.SaveGroupRequest](r)
		if err != nil {
			return response{}, err
		}
	}
	result, version, err := a.Service.Group(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.Method, input, v)
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	return response{Body: result, Version: &version}, err
}

func (a *API) groupMembers(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.GroupMembers(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), page, size)
	return response{Body: result}, err
}
func (a *API) leaveGroup(r *request) (response, error) {
	return response{Status: 204}, a.Service.RemoveGroupMember(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.Principal.MemberID)
}
func (a *API) removeGroupMember(r *request) (response, error) {
	return response{Status: 204}, a.Service.RemoveGroupMember(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("memberId"))
}
func (a *API) groupInvitations(r *request) (response, error) {
	group := r.PathValue("groupId")
	if r.Method == "GET" {
		page, size, err := pagination(r)
		if err != nil {
			return response{}, err
		}
		result, err := a.Service.GroupInvitations(r.Context(), r.Principal.MemberID, group, page, size)
		return response{Body: result}, err
	}
	input, err := body[domain.InviteGroupMemberRequest](r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.InviteGroupMember(r.Context(), r.Principal.MemberID, group, input)
	return response{Status: 201, Body: result, Location: "/api/v1/groups/" + group + "/invitations/" + result.Id}, err
}
func (a *API) receivedGroupInvitations(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.GroupInvitations(r.Context(), r.Principal.MemberID, "", page, size)
	return response{Body: result}, err
}
func (a *API) receivedGroupInvitation(r *request) (response, error) {
	result, err := a.Service.GroupInvitation(r.Context(), r.Principal.MemberID, "", r.PathValue("invitationId"))
	return response{Body: result}, err
}
func (a *API) groupInvitation(r *request) (response, error) {
	result, err := a.Service.GroupInvitation(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("invitationId"))
	return response{Body: result}, err
}
func (a *API) revokeGroupInvitation(r *request) (response, error) {
	return response{Status: 204}, a.Service.RevokeGroupInvitation(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("invitationId"))
}
func (a *API) answerGroupInvitation(r *request) (response, error) {
	result, err := a.Service.AnswerGroupInvitation(r.Context(), r.Principal.MemberID, r.PathValue("invitationId"), r.Method == "POST")
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	return response{Body: result}, err
}
func (a *API) groupWishlists(r *request) (response, error) {
	page, size, err := pagination(r)
	if err != nil {
		return response{}, err
	}
	result, err := a.Service.GroupWishlists(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), page, size)
	return response{Body: result}, err
}
func (a *API) groupWishlist(r *request) (response, error) {
	if r.Method == "GET" {
		available, err := validation.QueryBoolean(r.URL.Query(), "availableOnly")
		if err != nil {
			return response{}, err
		}
		result, err := a.Service.GroupWishlist(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId"), available)
		return response{Body: result}, err
	}
	result, err := a.Service.ShareGroupWishlist(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId"), r.Method == "DELETE")
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	return response{Status: 201, Body: result, Location: r.URL.Path}, err
}
func (a *API) groupWish(r *request) (response, error) {
	result, err := a.Service.GroupWish(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId"), r.PathValue("wishId"))
	return response{Body: result}, err
}
func (a *API) groupReservation(r *request) (response, error) {
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
	result, err := a.Service.GroupReservation(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), r.PathValue("wishlistId"), r.PathValue("wishId"), r.Method, quantity, v)
	if r.Method == "DELETE" {
		return response{Status: 204}, err
	}
	status, location := 200, ""
	if result.Created {
		status, location = 201, r.URL.Path
	}
	return response{Status: status, Body: result.Reservation, Version: &result.Version, Location: location}, err
}
func (a *API) groupWishImage(r *request) (response, error) {
	content, err := a.Service.ReadGroupWishImage(r.Context(), r.PathValue("groupId"), r.PathValue("wishlistId"), r.PathValue("wishId"), r.URL.Query().Get("token"))
	return response{Binary: content, MediaType: "image/webp"}, err
}

func (a *API) groupProfileImage(r *request) (response, error) {
	v, err := entityTag(r, true)
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
	result, version, err := a.Service.GroupProfileImage(r.Context(), r.Principal.MemberID, r.PathValue("groupId"), content, *v, r.Method == "DELETE")
	if r.Method == "DELETE" {
		return response{Status: 204, Version: &version}, err
	}
	return response{Body: result, Version: &version}, err
}

func (a *API) readGroupProfileImage(r *request) (response, error) {
	content, err := a.Service.ReadGroupProfileImage(r.Context(), r.PathValue("groupId"), r.URL.Query().Get("token"))
	return response{Binary: content, MediaType: "image/webp"}, err
}

func (a *API) wishlistGroupVisibility(r *request) (response, error) {
	if r.Method == "GET" {
		result, version, err := a.Service.WishlistGroupVisibility(r.Context(), r.Principal.MemberID, r.PathValue("wishlistId"))
		return response{Body: result, Version: &version}, err
	}
	expected, err := entityTag(r, true)
	if err != nil {
		return response{}, err
	}
	input, err := body[domain.SetWishlistGroupVisibilityRequest](r)
	if err != nil {
		return response{}, err
	}
	result, version, err := a.Service.SetWishlistGroupVisibility(r.Context(), r.Principal.MemberID, r.PathValue("wishlistId"), input, *expected)
	return response{Body: result, Version: &version}, err
}
