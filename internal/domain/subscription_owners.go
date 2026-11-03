package domain

// WishlistSubscriptionOwnerResponse identifies the verified owner of an authorized followed list.
type WishlistSubscriptionOwnerResponse struct {
	WishlistId string `json:"wishlistId"`
	OwnerId    string `json:"ownerId"`
}
