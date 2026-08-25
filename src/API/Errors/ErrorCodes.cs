using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Errors;
/// <summary>
/// Represents error codes.
/// </summary>

[ExcludeFromCodeCoverage]
public static class ErrorCodes
{
    /// <summary>
    /// Identifies account email not confirmed.
    /// </summary>
    #region Account

    public const string AccountEmailNotConfirmed = "ACCOUNT_EMAIL_NOT_CONFIRMED";
    /// <summary>
    /// Identifies account invalid credentials.
    /// </summary>
    public const string AccountInvalidCredentials = "ACCOUNT_INVALID_CREDENTIALS";
    /// <summary>
    /// Identifies an invalid authentication session.
    /// </summary>
    public const string AccountAuthenticationSessionInvalid = "ACCOUNT_AUTHENTICATION_SESSION_INVALID";
    /// <summary>
    /// Identifies account email confirmation invalid.
    /// </summary>
    public const string AccountEmailConfirmationInvalid = "ACCOUNT_EMAIL_CONFIRMATION_INVALID";
    /// <summary>
    /// Identifies an invalid account password reset link.
    /// </summary>
    public const string AccountPasswordResetInvalid = "ACCOUNT_PASSWORD_RESET_INVALID";
    #endregion

    #region Google

    /// <summary>
    /// Identifies an invalid or expired Google authentication flow.
    /// </summary>
    public const string GoogleAuthenticationFailed = "GOOGLE_AUTHENTICATION_FAILED";
    /// <summary>
    /// Identifies a failed explicit Google account link proof.
    /// </summary>
    public const string GoogleAccountLinkFailed = "GOOGLE_ACCOUNT_LINK_FAILED";
    /// <summary>
    /// Identifies a concurrent or ambiguous Google account link.
    /// </summary>
    public const string GoogleAccountLinkConflict = "GOOGLE_ACCOUNT_LINK_CONFLICT";

    #endregion

    #region Request

    /// <summary>
    /// Identifies request validation error.
    /// </summary>
    public const string RequestValidationError = "REQUEST_VALIDATION_ERROR";
    /// <summary>
    /// Identifies request rate limit exceeded.
    /// </summary>
    public const string RequestRateLimitExceeded = "REQUEST_RATE_LIMIT_EXCEEDED";
    /// <summary>
    /// Identifies request not found.
    /// </summary>
    public const string RequestNotFound = "REQUEST_NOT_FOUND";
    /// <summary>
    /// Identifies request bad request.
    /// </summary>
    public const string RequestBadRequest = "REQUEST_BAD_REQUEST";
    /// <summary>
    /// Identifies request payload too large.
    /// </summary>
    public const string RequestPayloadTooLarge = "REQUEST_PAYLOAD_TOO_LARGE";
    /// <summary>
    /// Identifies request unsupported media type.
    /// </summary>
    public const string RequestUnsupportedMediaType = "REQUEST_UNSUPPORTED_MEDIA_TYPE";
    /// <summary>
    /// Identifies a missing request precondition.
    /// </summary>
    public const string RequestPreconditionRequired = "REQUEST_PRECONDITION_REQUIRED";
    #endregion

    #region Security

    /// <summary>
    /// Identifies security unauthorized.
    /// </summary>
    public const string SecurityUnauthorized = "SECURITY_UNAUTHORIZED";
    /// <summary>
    /// Identifies security forbidden.
    /// </summary>
    public const string SecurityForbidden = "SECURITY_FORBIDDEN";
    #endregion

    #region Member

    /// <summary>
    /// Identifies a member profile version conflict.
    /// </summary>
    public const string MemberProfileVersionConflict = "MEMBER_PROFILE_VERSION_CONFLICT";
    /// <summary>
    /// Identifies an invalid current password for a sensitive member operation.
    /// </summary>
    public const string MemberCurrentPasswordInvalid = "MEMBER_CURRENT_PASSWORD_INVALID";
    /// <summary>
    /// Identifies an email address already assigned to another member.
    /// </summary>
    public const string MemberEmailAlreadyUsed = "MEMBER_EMAIL_ALREADY_USED";
    /// <summary>
    /// Identifies an invalid member email change confirmation.
    /// </summary>
    public const string MemberEmailChangeInvalid = "MEMBER_EMAIL_CHANGE_INVALID";

    #endregion

    #region Wishlist

    /// <summary>
    /// Identifies a private wishlist that is unavailable to the current member.
    /// </summary>
    public const string WishlistNotFound = "WISHLIST_NOT_FOUND";

    /// <summary>
    /// Identifies an owner-scoped wishlist name conflict.
    /// </summary>
    public const string WishlistNameAlreadyExists = "WISHLIST_NAME_ALREADY_EXISTS";

    /// <summary>
    /// Identifies an optimistic wishlist update conflict.
    /// </summary>
    public const string WishlistVersionConflict = "WISHLIST_VERSION_CONFLICT";

    /// <summary>
    /// Identifies a gift wish that is unavailable under its parent wishlist.
    /// </summary>
    public const string WishNotFound = "WISH_NOT_FOUND";

    #endregion

    #region Technical

    /// <summary>
    /// Identifies technical dependency unavailable.
    /// </summary>
    public const string TechnicalDependencyUnavailable = "TECHNICAL_DEPENDENCY_UNAVAILABLE";
    /// <summary>
    /// Identifies technical service unavailable.
    /// </summary>
    public const string TechnicalServiceUnavailable = "TECHNICAL_SERVICE_UNAVAILABLE";

    #endregion
}
