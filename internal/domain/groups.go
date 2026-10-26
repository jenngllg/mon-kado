package domain

import "time"

// SaveGroupRequest names a private group without changing its membership.
type SaveGroupRequest struct {
	Name *string `json:"name"`
}

// InviteGroupMemberRequest addresses an invitation to an existing member.
type InviteGroupMemberRequest struct {
	MemberId *string `json:"memberId"`
}

// GroupResponse describes a group visible only to its accepted members.
type GroupResponse struct {
	Id              string     `json:"id"`
	Name            string     `json:"name"`
	OwnerId         string     `json:"ownerId"`
	ProfileImageUrl *string    `json:"profileImageUrl"`
	MemberCount     int        `json:"memberCount"`
	CreatedAt       time.Time  `json:"createdAt"`
	UpdatedAt       *time.Time `json:"updatedAt"`
}

// GroupMemberResponse exposes public identity, never account credentials or email.
type GroupMemberResponse struct {
	Id              string    `json:"id"`
	DisplayName     string    `json:"displayName"`
	DisplayNameTag  string    `json:"displayNameTag"`
	ProfileImageUrl *string   `json:"profileImageUrl"`
	JoinedAt        time.Time `json:"joinedAt"`
}

// GroupInvitationResponse is visible to its recipient and accepted group members.
type GroupInvitationResponse struct {
	Id               string    `json:"id"`
	GroupId          string    `json:"groupId"`
	GroupName        string    `json:"groupName"`
	OwnerDisplayName string    `json:"ownerDisplayName"`
	MemberId         string    `json:"memberId"`
	DisplayName      string    `json:"displayName"`
	DisplayNameTag   string    `json:"displayNameTag"`
	CreatedAt        time.Time `json:"createdAt"`
	ExpiresAt        time.Time `json:"expiresAt"`
}

// GroupJoinLinkResponse contains a bearer code available only to accepted members.
type GroupJoinLinkResponse struct {
	GroupId   string    `json:"groupId"`
	Code      string    `json:"code"`
	JoinUrl   string    `json:"joinUrl"`
	CreatedAt time.Time `json:"createdAt"`
}

// GroupWishlistResponse describes an explicitly shared list without a public capability.
type GroupWishlistResponse struct {
	Id               string    `json:"id"`
	Name             string    `json:"name"`
	OwnerId          string    `json:"ownerId"`
	OwnerDisplayName string    `json:"ownerDisplayName"`
	Occasion         string    `json:"occasion"`
	EventDate        *string   `json:"eventDate"`
	CreatedAt        time.Time `json:"createdAt"`
}
