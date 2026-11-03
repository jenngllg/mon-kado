package domain

import "time"

// NotificationResponse describes an authorized in-app notification without share secrets.
type NotificationResponse struct {
	Id               string     `json:"id"`
	Kind             string     `json:"kind"`
	ActorDisplayName *string    `json:"actorDisplayName"`
	WishlistName     *string    `json:"wishlistName"`
	GroupName        *string    `json:"groupName"`
	WishName         *string    `json:"wishName"`
	Quantity         int        `json:"quantity"`
	DaysRemaining    *int       `json:"daysRemaining"`
	Href             string     `json:"href"`
	CreatedAt        time.Time  `json:"createdAt"`
	ReadAt           *time.Time `json:"readAt"`
}

// NotificationStatusResponse is the live unread indicator shared by all member sessions.
type NotificationStatusResponse struct {
	HasUnread bool `json:"hasUnread"`
}
