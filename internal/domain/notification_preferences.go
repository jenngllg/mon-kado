package domain

// NotificationPreferencesResponse contains the member's in-app notification choices.
type NotificationPreferencesResponse struct {
	WishesAdded            bool `json:"wishesAdded"`
	GroupJoined            bool `json:"groupJoined"`
	GroupInvitation        bool `json:"groupInvitation"`
	OccasionReminder       bool `json:"occasionReminder"`
	SharingReminder        bool `json:"sharingReminder"`
	GroupWishlistShared    bool `json:"groupWishlistShared"`
	ReservationUnavailable bool `json:"reservationUnavailable"`
	WishReserved           bool `json:"wishReserved"`
	SuggestionAdded        bool `json:"suggestionAdded"`
}

// UpdateNotificationPreferencesRequest distinguishes absent choices from explicit false values.
type UpdateNotificationPreferencesRequest struct {
	WishesAdded            *bool `json:"wishesAdded"`
	GroupJoined            *bool `json:"groupJoined"`
	GroupInvitation        *bool `json:"groupInvitation"`
	OccasionReminder       *bool `json:"occasionReminder"`
	SharingReminder        *bool `json:"sharingReminder"`
	GroupWishlistShared    *bool `json:"groupWishlistShared"`
	ReservationUnavailable *bool `json:"reservationUnavailable"`
	WishReserved           *bool `json:"wishReserved"`
	SuggestionAdded        *bool `json:"suggestionAdded"`
}
