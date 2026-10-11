package domain

import (
	"encoding/json"
	"time"
)

// CompleteGoogleSessionRequest preserves the versioned JSON contract.
type CompleteGoogleSessionRequest struct {
	Flow *string `json:"flow"`
}

// ConfirmEmailRequest preserves the versioned JSON contract.
type ConfirmEmailRequest struct {
	UserId *string `json:"userId"`
	Token  *string `json:"token"`
}

// ConfirmMemberAccountDeletionRequest preserves the versioned JSON contract.
type ConfirmMemberAccountDeletionRequest struct {
	Token *string `json:"token"`
}

// ConfirmMemberEmailChangeRequest preserves the versioned JSON contract.
type ConfirmMemberEmailChangeRequest struct {
	RequestId *string `json:"requestId"`
	Token     *string `json:"token"`
}

// CreateWishImportPreviewRequest preserves the versioned JSON contract.
type CreateWishImportPreviewRequest struct {
	Url *string `json:"url"`
}

// CreateWishlistRequest preserves the versioned JSON contract.
type CreateWishlistRequest struct {
	Name         *string `json:"name"`
	Occasion     *string `json:"occasion"`
	EventDate    *string `json:"eventDate"`
	Message      *string `json:"message"`
	SurpriseMode *bool   `json:"surpriseMode"`
}

// CreateWishRequest preserves the versioned JSON contract.
type CreateWishRequest struct {
	Name       *string      `json:"name"`
	Note       *string      `json:"note"`
	Url        *string      `json:"url"`
	Price      *json.Number `json:"price"`
	Quantity   *int         `json:"quantity"`
	IsFavorite *bool        `json:"isFavorite"`
}

// ExecuteAdministrativeAccountErasureRequest preserves the versioned JSON contract.
type ExecuteAdministrativeAccountErasureRequest struct {
	RequestReference  *string `json:"requestReference"`
	ConfirmedMemberId *string `json:"confirmedMemberId"`
}

// JoinSharedWishlistRequest preserves the versioned JSON contract.
type JoinSharedWishlistRequest struct {
	DisplayName *string `json:"displayName"`
}

// LinkGoogleAccountRequest preserves the versioned JSON contract.
type LinkGoogleAccountRequest struct {
	Flow            *string `json:"flow"`
	CurrentPassword *string `json:"currentPassword"`
}

// LoginRequest preserves the versioned JSON contract.
type LoginRequest struct {
	Email      *string `json:"email"`
	Password   *string `json:"password"`
	RememberMe bool    `json:"rememberMe"`
}

// RegisterAccountRequest preserves the versioned JSON contract.
type RegisterAccountRequest struct {
	Email       *string `json:"email"`
	Password    *string `json:"password"`
	DisplayName *string `json:"displayName"`
}

// ReorderWishesRequest preserves the versioned JSON contract.
type ReorderWishesRequest struct {
	WishIds []string `json:"wishIds"`
}

// ReportSharedWishlistRequest preserves the versioned JSON contract.
type ReportSharedWishlistRequest struct {
	Reason  *string `json:"reason"`
	Details *string `json:"details"`
}

// RequestAdministrativeDataExportRequest preserves the versioned JSON contract.
type RequestAdministrativeDataExportRequest struct {
	RequestReference *string `json:"requestReference"`
}

// RequestEmailConfirmationRequest preserves the versioned JSON contract.
type RequestEmailConfirmationRequest struct {
	Email *string `json:"email"`
}

// RequestPasswordResetRequest preserves the versioned JSON contract.
type RequestPasswordResetRequest struct {
	Email *string `json:"email"`
}

// ResetPasswordRequest preserves the versioned JSON contract.
type ResetPasswordRequest struct {
	UserId      *string `json:"userId"`
	Token       *string `json:"token"`
	NewPassword *string `json:"newPassword"`
}

// RevokeMemberSessionsRequest preserves the versioned JSON contract.
type RevokeMemberSessionsRequest struct {
	RequestReference  *string `json:"requestReference"`
	ConfirmedMemberId *string `json:"confirmedMemberId"`
}

// TwoFactorCompletionRequest preserves the versioned JSON contract.
type TwoFactorCompletionRequest struct {
	Flow         *string `json:"flow"`
	Code         *string `json:"code"`
	RecoveryCode *string `json:"recoveryCode"`
}

// TwoFactorFlowRequest preserves the versioned JSON contract.
type TwoFactorFlowRequest struct {
	Flow *string `json:"flow"`
}

// TwoFactorReauthenticationRequest preserves the versioned JSON contract.
type TwoFactorReauthenticationRequest struct {
	Purpose      *TwoFactorFlowPurpose `json:"purpose"`
	Code         *string               `json:"code"`
	RecoveryCode *string               `json:"recoveryCode"`
}

// TwoFactorSetupConfirmationRequest preserves the versioned JSON contract.
type TwoFactorSetupConfirmationRequest struct {
	Flow *string `json:"flow"`
	Code *string `json:"code"`
}

// UpdateMemberEmailRequest preserves the versioned JSON contract.
type UpdateMemberEmailRequest struct {
	Email           *string `json:"email"`
	CurrentPassword *string `json:"currentPassword"`
}

// UpdateMemberPasswordRequest preserves the versioned JSON contract.
type UpdateMemberPasswordRequest struct {
	CurrentPassword *string `json:"currentPassword"`
	NewPassword     *string `json:"newPassword"`
}

// UpdateMemberProfileRequest preserves the versioned JSON contract.
type UpdateMemberProfileRequest struct {
	DisplayName             *string `json:"displayName"`
	IsVisibleInMemberSearch *bool   `json:"isVisibleInMemberSearch"`
	HasVisibilityPreference bool    `json:"-"`
}

// UpdateWishlistModerationRequest preserves the versioned JSON contract.
type UpdateWishlistModerationRequest struct {
	IsSuspended *bool   `json:"isSuspended"`
	Reason      *string `json:"reason"`
}

// UpdateWishlistReportReviewRequest preserves the versioned JSON contract.
type UpdateWishlistReportReviewRequest struct {
	Status     *string `json:"status"`
	ReviewNote *string `json:"reviewNote"`
}

// UpdateWishlistRequest preserves the versioned JSON contract.
type UpdateWishlistRequest struct {
	Name         *string `json:"name"`
	Occasion     *string `json:"occasion"`
	EventDate    *string `json:"eventDate"`
	Message      *string `json:"message"`
	SurpriseMode *bool   `json:"surpriseMode"`
}

// UpdateWishRequest preserves the versioned JSON contract.
type UpdateWishRequest struct {
	Name       *string      `json:"name"`
	Note       *string      `json:"note"`
	Url        *string      `json:"url"`
	Price      *json.Number `json:"price"`
	Quantity   *int         `json:"quantity"`
	IsFavorite *bool        `json:"isFavorite"`
}

// SetWishlistArchivedRequest changes only the owner's archive preference.
type SetWishlistArchivedRequest struct {
	IsArchived *bool `json:"isArchived"`
}

// SetWishFavoriteRequest changes only the owner's favorite preference.
type SetWishFavoriteRequest struct {
	IsFavorite *bool `json:"isFavorite"`
}

// UpsertGiftReservationRequest preserves the versioned JSON contract.
type UpsertGiftReservationRequest struct {
	Quantity *int `json:"quantity"`
}

// AccessTokenResponse preserves the versioned JSON contract.
type AccessTokenResponse struct {
	AccessToken string `json:"accessToken"`
	TokenType   string `json:"tokenType"`
	ExpiresIn   int    `json:"expiresIn"`
}

// AdministrativeAuditEventResponse preserves the versioned JSON contract.
type AdministrativeAuditEventResponse struct {
	Id                       string                    `json:"id"`
	CreatedAt                time.Time                 `json:"createdAt"`
	Action                   AdministrativeAuditAction `json:"action"`
	AdministratorId          *string                   `json:"administratorId"`
	AdministratorDisplayName *string                   `json:"administratorDisplayName"`
	WishlistId               *string                   `json:"wishlistId"`
	MemberId                 *string                   `json:"memberId"`
	ExportId                 *string                   `json:"exportId"`
	Reason                   *string                   `json:"reason"`
	RequestReference         *string                   `json:"requestReference"`
}

// CsrfTokenResponse preserves the versioned JSON contract.
type CsrfTokenResponse struct {
	Token string `json:"token"`
}

// CurrentSessionResponse preserves the versioned JSON contract.
type CurrentSessionResponse struct {
	Id                      string   `json:"id"`
	Email                   string   `json:"email"`
	DisplayName             string   `json:"displayName"`
	ProfileImageUrl         *string  `json:"profileImageUrl"`
	Roles                   []string `json:"roles"`
	IsGoogleLinked          bool     `json:"isGoogleLinked"`
	IsVisibleInMemberSearch bool     `json:"isVisibleInMemberSearch"`
}

// GiftReservationHistoryResponse preserves the versioned JSON contract.
type GiftReservationHistoryResponse struct {
	OwnerId          *string    `json:"ownerId"`
	OwnerDisplayName *string    `json:"ownerDisplayName"`
	ShareUrl         *string    `json:"shareUrl"`
	ImageUrl         *string    `json:"imageUrl"`
	OwnedWishPath    *string    `json:"ownedWishPath"`
	IsArchived       bool       `json:"isArchived"`
	Id               string     `json:"id"`
	WishlistId       string     `json:"wishlistId"`
	WishlistName     string     `json:"wishlistName"`
	WishId           string     `json:"wishId"`
	WishName         string     `json:"wishName"`
	ShareLinkId      *string    `json:"shareLinkId"`
	Quantity         int        `json:"quantity"`
	Status           string     `json:"status"`
	CreatedAt        time.Time  `json:"createdAt"`
	LastActivityAt   time.Time  `json:"lastActivityAt"`
	EndedAt          *time.Time `json:"endedAt"`
}

// GiftReservationResponse preserves the versioned JSON contract.
type GiftReservationResponse struct {
	Id        string     `json:"id"`
	WishId    string     `json:"wishId"`
	Quantity  int        `json:"quantity"`
	CreatedAt time.Time  `json:"createdAt"`
	UpdatedAt *time.Time `json:"updatedAt"`
}

// MemberProfileResponse preserves the versioned JSON contract.
type MemberProfileResponse struct {
	DisplayName             string  `json:"displayName"`
	ProfileImageUrl         *string `json:"profileImageUrl"`
	IsVisibleInMemberSearch bool    `json:"isVisibleInMemberSearch"`
}

// PersonalDataExportResponse preserves the versioned JSON contract.
type PersonalDataExportResponse struct {
	Id          string                   `json:"id"`
	Status      PersonalDataExportStatus `json:"status"`
	CreatedAt   time.Time                `json:"createdAt"`
	SnapshotAt  *time.Time               `json:"snapshotAt"`
	ReadyAt     *time.Time               `json:"readyAt"`
	ExpiresAt   *time.Time               `json:"expiresAt"`
	SizeInBytes *int64                   `json:"sizeInBytes"`
	ErrorCode   *string                  `json:"errorCode"`
}

// ReportedWishlistResponse preserves the versioned JSON contract.
type ReportedWishlistResponse struct {
	WishlistId       string                 `json:"wishlistId"`
	Name             string                 `json:"name"`
	OwnerId          string                 `json:"ownerId"`
	OwnerDisplayName string                 `json:"ownerDisplayName"`
	Occasion         string                 `json:"occasion"`
	EventDate        *string                `json:"eventDate"`
	Message          *string                `json:"message"`
	CreatedAt        time.Time              `json:"createdAt"`
	UpdatedAt        *time.Time             `json:"updatedAt"`
	IsSuspended      bool                   `json:"isSuspended"`
	SuspensionReason *string                `json:"suspensionReason"`
	SuspendedAt      *time.Time             `json:"suspendedAt"`
	Wishes           []ReportedWishResponse `json:"wishes"`
}

// ReportedWishResponse preserves the versioned JSON contract.
type ReportedWishResponse struct {
	Id        string       `json:"id"`
	Name      string       `json:"name"`
	Note      *string      `json:"note"`
	Url       *string      `json:"url"`
	Price     *json.Number `json:"price"`
	Quantity  int          `json:"quantity"`
	Position  int64        `json:"position"`
	CreatedAt time.Time    `json:"createdAt"`
	UpdatedAt *time.Time   `json:"updatedAt"`
	ImageUrl  *string      `json:"imageUrl"`
}

// SharedWishDetailResponse preserves the versioned JSON contract.
type SharedWishDetailResponse struct {
	Id                                 string       `json:"id"`
	Name                               string       `json:"name"`
	Note                               *string      `json:"note"`
	Url                                *string      `json:"url"`
	Price                              *json.Number `json:"price"`
	Quantity                           int          `json:"quantity"`
	IsFavorite                         bool         `json:"isFavorite"`
	ReservedQuantity                   *int         `json:"reservedQuantity"`
	AvailableQuantity                  *int         `json:"availableQuantity"`
	CurrentParticipantReservedQuantity *int         `json:"currentParticipantReservedQuantity"`
	ImageUrl                           *string      `json:"imageUrl"`
}

// SharedWishlistResponse preserves the versioned JSON contract.
type SharedWishlistResponse struct {
	CanSubscribe       bool                         `json:"canSubscribe"`
	Id                 string                       `json:"id"`
	OwnerDisplayName   string                       `json:"ownerDisplayName"`
	Name               string                       `json:"name"`
	Occasion           string                       `json:"occasion"`
	EventDate          *string                      `json:"eventDate"`
	Message            *string                      `json:"message"`
	Wishes             []SharedWishResponse         `json:"wishes"`
	CurrentParticipant *WishlistParticipantResponse `json:"currentParticipant"`
}

// SharedWishResponse preserves the versioned JSON contract.
type SharedWishResponse struct {
	Id                                 string       `json:"id"`
	Name                               string       `json:"name"`
	Url                                *string      `json:"url"`
	Price                              *json.Number `json:"price"`
	Quantity                           int          `json:"quantity"`
	IsFavorite                         bool         `json:"isFavorite"`
	ReservedQuantity                   *int         `json:"reservedQuantity"`
	AvailableQuantity                  *int         `json:"availableQuantity"`
	CurrentParticipantReservedQuantity *int         `json:"currentParticipantReservedQuantity"`
	ImageUrl                           *string      `json:"imageUrl"`
}

// UserSearchResponse preserves the versioned JSON contract.
type UserSearchResponse struct {
	Id              string  `json:"id"`
	DisplayName     string  `json:"displayName"`
	ProfileImageUrl *string `json:"profileImageUrl"`
}

// PublicMemberProfileResponse exposes a confirmed identity and its discoverable shared lists.
type PublicMemberProfileResponse struct {
	Id              string                         `json:"id"`
	DisplayName     string                         `json:"displayName"`
	ProfileImageUrl *string                        `json:"profileImageUrl"`
	Wishlists       []PublicMemberWishlistResponse `json:"wishlists"`
}

// PublicMemberWishlistResponse contains only the public metadata and active frontend capability.
type PublicMemberWishlistResponse struct {
	Id        string  `json:"id"`
	Name      string  `json:"name"`
	Occasion  string  `json:"occasion"`
	EventDate *string `json:"eventDate"`
	ShareUrl  string  `json:"shareUrl"`
}

// WishlistSubscriptionResponse exposes accessible followed-list metadata without participant data.
type WishlistSubscriptionResponse struct {
	Id               string    `json:"id"`
	WishlistId       string    `json:"wishlistId"`
	Name             string    `json:"name"`
	OwnerDisplayName string    `json:"ownerDisplayName"`
	Occasion         string    `json:"occasion"`
	EventDate        *string   `json:"eventDate"`
	CreatedAt        time.Time `json:"createdAt"`
	ShareUrl         string    `json:"shareUrl"`
}

// WishCollectionItemResponse preserves the versioned JSON contract.
type WishCollectionItemResponse struct {
	Id                string       `json:"id"`
	WishlistId        string       `json:"wishlistId"`
	Name              string       `json:"name"`
	Note              *string      `json:"note"`
	Url               *string      `json:"url"`
	Price             *json.Number `json:"price"`
	Quantity          int          `json:"quantity"`
	IsFavorite        bool         `json:"isFavorite"`
	ReservedQuantity  *int         `json:"reservedQuantity"`
	AvailableQuantity *int         `json:"availableQuantity"`
	ImageUrl          *string      `json:"imageUrl"`
	Position          int64        `json:"position"`
	CreatedAt         time.Time    `json:"createdAt"`
	UpdatedAt         *time.Time   `json:"updatedAt"`
	EntityTag         string       `json:"entityTag"`
}

// WishCollectionResponse preserves the versioned JSON contract.
type WishCollectionResponse struct {
	Wishes []WishCollectionItemResponse `json:"wishes"`
}

// WishlistModerationEventResponse preserves the versioned JSON contract.
type WishlistModerationEventResponse struct {
	Id              string    `json:"id"`
	AdministratorId *string   `json:"administratorId"`
	Action          string    `json:"action"`
	Reason          *string   `json:"reason"`
	OccurredAt      time.Time `json:"occurredAt"`
}

// WishlistModerationResponse preserves the versioned JSON contract.
type WishlistModerationResponse struct {
	WishlistId       string     `json:"wishlistId"`
	IsSuspended      bool       `json:"isSuspended"`
	SuspensionReason *string    `json:"suspensionReason"`
	SuspendedAt      *time.Time `json:"suspendedAt"`
}

// WishlistParticipantResponse preserves the versioned JSON contract.
type WishlistParticipantResponse struct {
	Id          string `json:"id"`
	DisplayName string `json:"displayName"`
}

// WishlistResponse preserves the versioned JSON contract.
type WishlistResponse struct {
	Id               string     `json:"id"`
	Name             string     `json:"name"`
	Occasion         string     `json:"occasion"`
	EventDate        *string    `json:"eventDate"`
	Message          *string    `json:"message"`
	CreatedAt        time.Time  `json:"createdAt"`
	UpdatedAt        *time.Time `json:"updatedAt"`
	SurpriseMode     bool       `json:"surpriseMode"`
	IsArchived       bool       `json:"isArchived"`
	IsSuspended      bool       `json:"isSuspended"`
	SuspensionReason *string    `json:"suspensionReason"`
	SuspendedAt      *time.Time `json:"suspendedAt"`
}

// WishlistShareLinkResponse preserves the versioned JSON contract.
type WishlistShareLinkResponse struct {
	Id        string     `json:"id"`
	ShareUrl  string     `json:"shareUrl"`
	CreatedAt time.Time  `json:"createdAt"`
	UpdatedAt *time.Time `json:"updatedAt"`
}

// WishOrderItemResponse preserves the versioned JSON contract.
type WishOrderItemResponse struct {
	Id        string `json:"id"`
	Position  int64  `json:"position"`
	EntityTag string `json:"entityTag"`
}

// WishOrderResponse preserves the versioned JSON contract.
type WishOrderResponse struct {
	Wishes []WishOrderItemResponse `json:"wishes"`
}

// WishResponse preserves the versioned JSON contract.
type WishResponse struct {
	Id                string       `json:"id"`
	WishlistId        string       `json:"wishlistId"`
	Name              string       `json:"name"`
	Note              *string      `json:"note"`
	Url               *string      `json:"url"`
	Price             *json.Number `json:"price"`
	Quantity          int          `json:"quantity"`
	IsFavorite        bool         `json:"isFavorite"`
	ReservedQuantity  *int         `json:"reservedQuantity"`
	AvailableQuantity *int         `json:"availableQuantity"`
	Position          int64        `json:"position"`
	CreatedAt         time.Time    `json:"createdAt"`
	UpdatedAt         *time.Time   `json:"updatedAt"`
	ImageUrl          *string      `json:"imageUrl"`
}

// TwoFactorChallengeResponse preserves the versioned JSON contract.
type TwoFactorChallengeResponse struct {
	Flow           string                  `json:"flow"`
	RequiredAction TwoFactorRequiredAction `json:"requiredAction"`
	ExpiresAt      time.Time               `json:"expiresAt"`
}

// TwoFactorRecoveryCodesResponse preserves the versioned JSON contract.
type TwoFactorRecoveryCodesResponse struct {
	RecoveryCodes []string `json:"recoveryCodes"`
}

// TwoFactorSetupResponse preserves the versioned JSON contract.
type TwoFactorSetupResponse struct {
	ManualKey  string `json:"manualKey"`
	OtpAuthUri string `json:"otpAuthUri"`
}

// TwoFactorStatusResponse preserves the versioned JSON contract.
type TwoFactorStatusResponse struct {
	IsEnabled              bool `json:"isEnabled"`
	RemainingRecoveryCodes int  `json:"remainingRecoveryCodes"`
}

// WishlistReportDetails preserves the versioned JSON contract.
type WishlistReportDetails struct {
	Status                    string     `json:"status"`
	ReviewNote                *string    `json:"reviewNote"`
	ReviewedAt                *time.Time `json:"reviewedAt"`
	ReviewedByAdministratorId *string    `json:"reviewedByAdministratorId"`
	Id                        string     `json:"id"`
	Reason                    string     `json:"reason"`
	Details                   *string    `json:"details"`
	CreatedAt                 time.Time  `json:"createdAt"`
}

// WishlistReportReviewEventDetails preserves the versioned JSON contract.
type WishlistReportReviewEventDetails struct {
	Id              string    `json:"id"`
	PreviousStatus  string    `json:"previousStatus"`
	Status          string    `json:"status"`
	Note            *string   `json:"note"`
	AdministratorId *string   `json:"administratorId"`
	OccurredAt      time.Time `json:"occurredAt"`
}

// CopyOwnedWishRequest identifies the owned destination for an independent wish copy.
type CopyOwnedWishRequest struct {
	DestinationWishlistId *string `json:"destinationWishlistId"`
}

// CopyWishRequest identifies a capability-authorized shared wish to copy.
type CopyWishRequest struct {
	SourceShareLinkId *string `json:"sourceShareLinkId"`
	SourceWishId      *string `json:"sourceWishId"`
}
