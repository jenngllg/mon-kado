package domain

// Stable error codes shared with API consumers.
const (
	GroupNotFound                          = "GROUP_NOT_FOUND"
	GroupImageNotFound                     = "GROUP_IMAGE_NOT_FOUND"
	GroupAccessDenied                      = "GROUP_ACCESS_DENIED"
	GroupVersionConflict                   = "GROUP_VERSION_CONFLICT"
	GroupMemberNotFound                    = "GROUP_MEMBER_NOT_FOUND"
	GroupMemberAlreadyExists               = "GROUP_MEMBER_ALREADY_EXISTS"
	GroupInvitationNotFound                = "GROUP_INVITATION_NOT_FOUND"
	GroupInvitationAlreadyExists           = "GROUP_INVITATION_ALREADY_EXISTS"
	GroupJoinLinkNotFound                  = "GROUP_JOIN_LINK_NOT_FOUND"
	GroupJoinLinkAlreadyExists             = "GROUP_JOIN_LINK_ALREADY_EXISTS"
	GroupJoinLinkVersionConflict           = "GROUP_JOIN_LINK_VERSION_CONFLICT"
	GroupOwnerCannotLeave                  = "GROUP_OWNER_CANNOT_LEAVE"
	GroupLimitReached                      = "GROUP_LIMIT_REACHED"
	GroupWishlistAlreadyExists             = "GROUP_WISHLIST_ALREADY_EXISTS"
	GroupWishlistNotFound                  = "GROUP_WISHLIST_NOT_FOUND"
	GroupOwnerWishlist                     = "GROUP_OWNER_WISHLIST"
	MemberDataExportNotFound               = "MEMBER_DATA_EXPORT_NOT_FOUND"
	MemberDataExportNotReady               = "MEMBER_DATA_EXPORT_NOT_READY"
	MemberDataExportRateLimited            = "MEMBER_DATA_EXPORT_RATE_LIMITED"
	MemberDataExportGenerationFailed       = "MEMBER_DATA_EXPORT_GENERATION_FAILED"
	MemberDataExportTooLarge               = "MEMBER_DATA_EXPORT_TOO_LARGE"
	WishImportUrlRejected                  = "WISH_IMPORT_URL_REJECTED"
	AccountTwoFactorAuthenticationFailed   = "ACCOUNT_TWO_FACTOR_AUTHENTICATION_FAILED"
	AccountTwoFactorOperationConflict      = "ACCOUNT_TWO_FACTOR_OPERATION_CONFLICT"
	AccountTwoFactorRateLimited            = "ACCOUNT_TWO_FACTOR_RATE_LIMITED"
	AccountTwoFactorAccessDenied           = "ACCOUNT_TWO_FACTOR_ACCESS_DENIED"
	AccountSelfErasureNotAllowed           = "ACCOUNT_SELF_ERASURE_NOT_ALLOWED"
	AccountSelfSessionRevocationNotAllowed = "ACCOUNT_SELF_SESSION_REVOCATION_NOT_ALLOWED"
	AccountSessionRevocationTargetNotFound = "ACCOUNT_SESSION_REVOCATION_TARGET_NOT_FOUND"
	AccountErasureTargetNotFound           = "ACCOUNT_ERASURE_TARGET_NOT_FOUND"
	ProfileImageUnsupportedFormat          = "ACCOUNT_PROFILE_IMAGE_UNSUPPORTED_FORMAT"
	ProfileImageInvalid                    = "ACCOUNT_PROFILE_IMAGE_INVALID"
	ProfileImageNotFound                   = "ACCOUNT_PROFILE_IMAGE_NOT_FOUND"
	AccountEmailNotConfirmed               = "ACCOUNT_EMAIL_NOT_CONFIRMED"
	AccountPublicProfileNotFound           = "ACCOUNT_PUBLIC_PROFILE_NOT_FOUND"
	AccountDeletionInvalid                 = "ACCOUNT_DELETION_INVALID"
	AccountDeletionRateLimited             = "ACCOUNT_DELETION_RATE_LIMITED"
	AccountInvalidCredentials              = "ACCOUNT_INVALID_CREDENTIALS"
	AccountAuthenticationSessionInvalid    = "ACCOUNT_AUTHENTICATION_SESSION_INVALID"
	PublicMemberProfileNotFound            = "ACCOUNT_PUBLIC_PROFILE_NOT_FOUND"
	AccountEmailConfirmationInvalid        = "ACCOUNT_EMAIL_CONFIRMATION_INVALID"
	AccountPasswordResetInvalid            = "ACCOUNT_PASSWORD_RESET_INVALID"
	GoogleAuthenticationFailed             = "GOOGLE_AUTHENTICATION_FAILED"
	GoogleAccountLinkFailed                = "GOOGLE_ACCOUNT_LINK_FAILED"
	GoogleAccountLinkConflict              = "GOOGLE_ACCOUNT_LINK_CONFLICT"
	GoogleAccountLinkRequired              = "GOOGLE_ACCOUNT_LINK_REQUIRED"
	GoogleAdditionalVerificationRequired   = "GOOGLE_ADDITIONAL_VERIFICATION_REQUIRED"
	RequestValidationError                 = "REQUEST_VALIDATION_ERROR"
	RequestRateLimitExceeded               = "REQUEST_RATE_LIMIT_EXCEEDED"
	RequestNotFound                        = "REQUEST_NOT_FOUND"
	RequestBadRequest                      = "REQUEST_BAD_REQUEST"
	RequestPayloadTooLarge                 = "REQUEST_PAYLOAD_TOO_LARGE"
	RequestUnsupportedMediaType            = "REQUEST_UNSUPPORTED_MEDIA_TYPE"
	RequestPreconditionRequired            = "REQUEST_PRECONDITION_REQUIRED"
	SecurityUnauthorized                   = "SECURITY_UNAUTHORIZED"
	SecurityForbidden                      = "SECURITY_FORBIDDEN"
	MemberDisplayNameTagUnavailable        = "MEMBER_DISPLAY_NAME_TAG_UNAVAILABLE"
	MemberHandleAlreadyUsed                = "MEMBER_HANDLE_ALREADY_USED"
	MemberProfileVersionConflict           = "MEMBER_PROFILE_VERSION_CONFLICT"
	MemberCurrentPasswordInvalid           = "MEMBER_CURRENT_PASSWORD_INVALID"
	MemberEmailAlreadyUsed                 = "MEMBER_EMAIL_ALREADY_USED"
	MemberEmailChangeInvalid               = "MEMBER_EMAIL_CHANGE_INVALID"
	WishlistSuspended                      = "WISHLIST_SUSPENDED"
	WishlistArchived                       = "WISHLIST_ARCHIVED"
	WishlistSubscriptionNotFound           = "WISHLIST_SUBSCRIPTION_NOT_FOUND"
	WishlistSubscriptionAlreadyExists      = "WISHLIST_SUBSCRIPTION_ALREADY_EXISTS"
	WishlistSubscriptionSelf               = "WISHLIST_SUBSCRIPTION_SELF"
	WishlistModerationForbidden            = "WISHLIST_MODERATION_FORBIDDEN"
	WishlistReportNotFound                 = "WISHLIST_REPORT_NOT_FOUND"
	WishlistReportVersionConflict          = "WISHLIST_REPORT_VERSION_CONFLICT"
	WishlistNotFound                       = "WISHLIST_NOT_FOUND"
	WishlistNameAlreadyExists              = "WISHLIST_NAME_ALREADY_EXISTS"
	WishlistVersionConflict                = "WISHLIST_VERSION_CONFLICT"
	WishNotFound                           = "WISH_NOT_FOUND"
	WishVersionConflict                    = "WISH_VERSION_CONFLICT"
	WishOrderConflict                      = "WISH_ORDER_CONFLICT"
	WishOrderVersionConflict               = "WISH_ORDER_VERSION_CONFLICT"
	WishLimitReached                       = "WISH_LIMIT_REACHED"
	WishQuantityBelowReserved              = "WISH_QUANTITY_BELOW_RESERVED"
	WishImageInvalid                       = "WISH_IMAGE_INVALID"
	WishImageUnsupportedFormat             = "WISH_IMAGE_UNSUPPORTED_FORMAT"
	WishImageNotFound                      = "WISH_IMAGE_NOT_FOUND"
	WishlistShareLinkNotFound              = "WISHLIST_SHARE_LINK_NOT_FOUND"
	WishlistShareLinkAlreadyExists         = "WISHLIST_SHARE_LINK_ALREADY_EXISTS"
	WishlistShareLinkVersionConflict       = "WISHLIST_SHARE_LINK_VERSION_CONFLICT"
	SharedWishlistNotFound                 = "SHARED_WISHLIST_NOT_FOUND"
	SharedWishNotFound                     = "SHARED_WISH_NOT_FOUND"
	WishlistParticipantNotFound            = "WISHLIST_PARTICIPANT_NOT_FOUND"
	WishlistOwnerCannotJoin                = "WISHLIST_OWNER_CANNOT_JOIN"
	WishlistParticipantLimitReached        = "WISHLIST_PARTICIPANT_LIMIT_REACHED"
	GuestSessionInvalid                    = "GUEST_SESSION_INVALID"
	GiftReservationNotFound                = "GIFT_RESERVATION_NOT_FOUND"
	GiftReservationQuantityUnavailable     = "GIFT_RESERVATION_QUANTITY_UNAVAILABLE"
	GiftReservationVersionConflict         = "GIFT_RESERVATION_VERSION_CONFLICT"
	TechnicalDependencyUnavailable         = "TECHNICAL_DEPENDENCY_UNAVAILABLE"
	TechnicalServiceUnavailable            = "TECHNICAL_SERVICE_UNAVAILABLE"
)

// KnownErrorCode reports whether a value belongs to the compiled business error vocabulary.
func KnownErrorCode(code string) bool {
	switch code {
	case MemberDataExportNotFound,
		MemberDataExportNotReady,
		MemberDataExportRateLimited,
		MemberDataExportGenerationFailed,
		MemberDataExportTooLarge,
		WishImportUrlRejected,
		AccountTwoFactorAuthenticationFailed,
		AccountTwoFactorOperationConflict,
		AccountTwoFactorRateLimited,
		AccountTwoFactorAccessDenied,
		AccountSelfErasureNotAllowed,
		AccountSelfSessionRevocationNotAllowed,
		AccountSessionRevocationTargetNotFound,
		AccountErasureTargetNotFound,
		ProfileImageUnsupportedFormat,
		ProfileImageInvalid,
		ProfileImageNotFound,
		AccountEmailNotConfirmed,
		AccountDeletionInvalid,
		AccountDeletionRateLimited,
		AccountInvalidCredentials,
		AccountAuthenticationSessionInvalid,
		PublicMemberProfileNotFound,
		AccountEmailConfirmationInvalid,
		AccountPasswordResetInvalid,
		GoogleAuthenticationFailed,
		GoogleAccountLinkFailed,
		GoogleAccountLinkConflict,
		GoogleAccountLinkRequired,
		GoogleAdditionalVerificationRequired,
		RequestValidationError,
		RequestRateLimitExceeded,
		RequestNotFound,
		RequestBadRequest,
		RequestPayloadTooLarge,
		RequestUnsupportedMediaType,
		RequestPreconditionRequired,
		SecurityUnauthorized,
		SecurityForbidden,
		MemberDisplayNameTagUnavailable,
		MemberHandleAlreadyUsed,
		MemberProfileVersionConflict,
		MemberCurrentPasswordInvalid,
		MemberEmailAlreadyUsed,
		MemberEmailChangeInvalid,
		WishlistSuspended,
		WishlistArchived,
		WishlistSubscriptionNotFound,
		WishlistSubscriptionAlreadyExists,
		WishlistSubscriptionSelf,
		WishlistModerationForbidden,
		WishlistReportNotFound,
		WishlistReportVersionConflict,
		WishlistNotFound,
		WishlistNameAlreadyExists,
		WishlistVersionConflict,
		WishNotFound,
		WishVersionConflict,
		WishOrderConflict,
		WishOrderVersionConflict,
		WishLimitReached,
		WishQuantityBelowReserved,
		WishImageInvalid,
		WishImageUnsupportedFormat,
		WishImageNotFound,
		WishlistShareLinkNotFound,
		WishlistShareLinkAlreadyExists,
		WishlistShareLinkVersionConflict,
		SharedWishlistNotFound,
		SharedWishNotFound,
		WishlistParticipantNotFound,
		WishlistOwnerCannotJoin,
		WishlistParticipantLimitReached,
		GuestSessionInvalid,
		GiftReservationNotFound,
		GiftReservationQuantityUnavailable,
		GiftReservationVersionConflict,
		TechnicalDependencyUnavailable,
		TechnicalServiceUnavailable:
		return true
	default:
		return false
	}
}

// WishlistGroupVisibilityVersionConflict identifies a stale owner audience version.
const WishlistGroupVisibilityVersionConflict = "WISHLIST_GROUP_VISIBILITY_VERSION_CONFLICT"

// WishImportPriceMayBeStale identifies a cached provider price that may have changed.
const WishImportPriceMayBeStale = "WISH_IMPORT_PRICE_MAY_BE_STALE"
