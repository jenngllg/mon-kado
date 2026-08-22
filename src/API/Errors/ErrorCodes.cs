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
    /// Identifies request validation error.
    /// </summary>

    #endregion

    #region Request

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
    /// Identifies security unauthorized.
    /// </summary>

    #endregion

    #region Security

    public const string SecurityUnauthorized = "SECURITY_UNAUTHORIZED";
    /// <summary>
    /// Identifies security forbidden.
    /// </summary>
    public const string SecurityForbidden = "SECURITY_FORBIDDEN";
    /// <summary>
    /// Identifies technical dependency unavailable.
    /// </summary>

    #endregion

    #region Technical

    public const string TechnicalDependencyUnavailable = "TECHNICAL_DEPENDENCY_UNAVAILABLE";
    /// <summary>
    /// Identifies technical internal server error.
    /// </summary>
    public const string TechnicalInternalServerError = "TECHNICAL_INTERNAL_SERVER_ERROR";
    /// <summary>
    /// Identifies technical service unavailable.
    /// </summary>
    public const string TechnicalServiceUnavailable = "TECHNICAL_SERVICE_UNAVAILABLE";

    #endregion
}
