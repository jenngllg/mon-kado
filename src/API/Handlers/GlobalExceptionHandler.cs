using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Logging;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Api.Handlers;
/// <summary>
/// Represents global exception handler.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="refreshTokenCookieService">The refresh token cookie service.</param>
/// <param name="googleExternalAuthenticationService">The protected Google external cookie service.</param>
/// <param name="guestSessionCookieService">The guest-session cookie service.</param>

public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IRefreshTokenCookieService refreshTokenCookieService,
    IGoogleExternalAuthenticationService googleExternalAuthenticationService,
    IGuestSessionCookieService guestSessionCookieService) : IExceptionHandler
{
    /// <summary>
    /// Executes the try handle async operation.
    /// </summary>
    /// <param name="httpContext">The http context.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">The request is canceled.</exception>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var response = exception switch
        {
            EmailNotConfirmedException => new ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                "Confirm your email address before signing in.",
                ErrorCodes.AccountEmailNotConfirmed,
                null),
            InvalidCredentialsException => new ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                "The email address or password is invalid.",
                ErrorCodes.AccountInvalidCredentials,
                null),
            InvalidAuthenticationSessionException => new ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "Authentication failed",
                "The authentication session is invalid or expired.",
                ErrorCodes.AccountAuthenticationSessionInvalid,
                null),
            GoogleAuthenticationFailedException => new ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "Google authentication failed",
                "The Google authentication flow is invalid or expired.",
                ErrorCodes.GoogleAuthenticationFailed,
                null),
            GoogleAccountLinkFailedException => new ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "Google account link failed",
                "The Google account could not be linked.",
                ErrorCodes.GoogleAccountLinkFailed,
                null),
            GoogleAccountLinkConflictException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Google account link conflict",
                "The Google account link conflicts with the current account state.",
                ErrorCodes.GoogleAccountLinkConflict,
                null),
            EmailConfirmationInvalidException => new ErrorResponse(
                StatusCodes.Status400BadRequest,
                "Email confirmation failed",
                "The email confirmation link is invalid or expired.",
                ErrorCodes.AccountEmailConfirmationInvalid,
                null),
            PasswordResetInvalidException => new ErrorResponse(
                StatusCodes.Status400BadRequest,
                "Password reset failed",
                "The password reset link is invalid or expired.",
                ErrorCodes.AccountPasswordResetInvalid,
                null),
            RequestValidationException validationException => new ErrorResponse(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "One or more fields are invalid.",
                ErrorCodes.RequestValidationError,
                validationException.ValidationErrors),
            BadHttpRequestException badRequestException
                when badRequestException.StatusCode == StatusCodes.Status413PayloadTooLarge =>
                new ErrorResponse(
                    StatusCodes.Status413PayloadTooLarge,
                    "Payload too large",
                    "The request body is too large.",
                    ErrorCodes.RequestPayloadTooLarge,
                    null),
            MemberProfileVersionConflictException => new ErrorResponse(
                StatusCodes.Status412PreconditionFailed,
                "Profile update conflict",
                "The member profile has changed. Retrieve it again before retrying.",
                ErrorCodes.MemberProfileVersionConflict,
                null),
            CurrentPasswordInvalidException => new ErrorResponse(
                StatusCodes.Status403Forbidden,
                "Current password verification failed",
                "The current password is invalid.",
                ErrorCodes.MemberCurrentPasswordInvalid,
                null),
            MemberEmailAlreadyUsedException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Email address unavailable",
                "The email address is already used by another account.",
                ErrorCodes.MemberEmailAlreadyUsed,
                null),
            MemberEmailChangeInvalidException => new ErrorResponse(
                StatusCodes.Status400BadRequest,
                "Email change confirmation failed",
                "The email change confirmation link is invalid or expired.",
                ErrorCodes.MemberEmailChangeInvalid,
                null),
            WishlistNotFoundException => new ErrorResponse(
                StatusCodes.Status404NotFound,
                "Wishlist not found",
                "The wishlist was not found.",
                ErrorCodes.WishlistNotFound,
                null),
            WishlistNameAlreadyExistsException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Wishlist name unavailable",
                "A wishlist with this name already exists.",
                ErrorCodes.WishlistNameAlreadyExists,
                null),
            WishlistVersionConflictException => new ErrorResponse(
                StatusCodes.Status412PreconditionFailed,
                "Wishlist version conflict",
                "The wishlist has changed. Retrieve it again before retrying.",
                ErrorCodes.WishlistVersionConflict,
                null),
            WishNotFoundException => new ErrorResponse(
                StatusCodes.Status404NotFound,
                "Wish not found",
                "The wish was not found under the requested wishlist.",
                ErrorCodes.WishNotFound,
                null),
            WishVersionConflictException => new ErrorResponse(
                StatusCodes.Status412PreconditionFailed,
                "Wish version conflict",
                "The wish has changed. Retrieve it again before retrying.",
                ErrorCodes.WishVersionConflict,
                null),
            WishOrderConflictException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Wish order conflict",
                "The requested order does not match the current wish collection.",
                ErrorCodes.WishOrderConflict,
                null),
            WishOrderVersionConflictException => new ErrorResponse(
                StatusCodes.Status412PreconditionFailed,
                "Wish order version conflict",
                "The wish collection has changed. Retrieve it again before retrying.",
                ErrorCodes.WishOrderVersionConflict,
                null),
            WishLimitReachedException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Wish limit reached",
                "The wishlist already contains the maximum number of wishes.",
                ErrorCodes.WishLimitReached,
                null),
            WishlistShareLinkNotFoundException => new ErrorResponse(
                StatusCodes.Status404NotFound,
                "Wishlist share link not found",
                "The wishlist share link was not found.",
                ErrorCodes.WishlistShareLinkNotFound,
                null),
            WishlistShareLinkAlreadyExistsException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Wishlist share link already exists",
                "The wishlist already has an active share link.",
                ErrorCodes.WishlistShareLinkAlreadyExists,
                null),
            WishlistShareLinkVersionConflictException => new ErrorResponse(
                StatusCodes.Status412PreconditionFailed,
                "Wishlist share link version conflict",
                "The wishlist share link has changed. Retrieve it again before retrying.",
                ErrorCodes.WishlistShareLinkVersionConflict,
                null),
            SharedWishlistNotFoundException => new ErrorResponse(
                StatusCodes.Status404NotFound,
                "Shared wishlist not found",
                "The shared wishlist was not found.",
                ErrorCodes.SharedWishlistNotFound,
                null),
            GuestSessionInvalidException => new ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "Guest session invalid",
                "The guest session is invalid or expired.",
                ErrorCodes.GuestSessionInvalid,
                null),
            WishlistParticipantNotFoundException => new ErrorResponse(
                StatusCodes.Status404NotFound,
                "Wishlist participant not found",
                "The current identity has not joined the shared wishlist.",
                ErrorCodes.WishlistParticipantNotFound,
                null),
            WishlistOwnerCannotJoinException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Wishlist owner cannot join",
                "The wishlist owner cannot join their own wishlist as a participant.",
                ErrorCodes.WishlistOwnerCannotJoin,
                null),
            WishlistParticipantLimitReachedException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Wishlist participant limit reached",
                "The wishlist already contains the maximum number of active participants.",
                ErrorCodes.WishlistParticipantLimitReached,
                null),
            GiftReservationNotFoundException => new ErrorResponse(
                StatusCodes.Status404NotFound,
                "Gift reservation not found",
                "The current participant has no reservation for the requested gift.",
                ErrorCodes.GiftReservationNotFound,
                null),
            GiftReservationQuantityUnavailableException => new ErrorResponse(
                StatusCodes.Status409Conflict,
                "Gift reservation quantity unavailable",
                "The requested gift quantity is no longer available.",
                ErrorCodes.GiftReservationQuantityUnavailable,
                null),
            GiftReservationVersionConflictException => new ErrorResponse(
                StatusCodes.Status412PreconditionFailed,
                "Gift reservation version conflict",
                "The gift reservation has changed. Retrieve it again before retrying.",
                ErrorCodes.GiftReservationVersionConflict,
                null),
            PreconditionRequiredException => new ErrorResponse(
                StatusCodes.Status428PreconditionRequired,
                "Precondition required",
                "The If-Match header is required.",
                ErrorCodes.RequestPreconditionRequired,
                null),
            DependencyUnavailableException => new ErrorResponse(
                StatusCodes.Status503ServiceUnavailable,
                "Service temporarily unavailable",
                "A required service is temporarily unavailable. Retry later.",
                ErrorCodes.TechnicalDependencyUnavailable,
                null),
            _ => new ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                "An unexpected error occurred.",
                null,
                null)
        };

        LogException(
            response,
            exception);

        if (exception is InvalidAuthenticationSessionException)
            refreshTokenCookieService.Delete(httpContext);

        if (exception is GuestSessionInvalidException)
            guestSessionCookieService.Delete(httpContext);

        if (exception is GoogleAuthenticationFailedException or GoogleAccountLinkConflictException)
            await googleExternalAuthenticationService.DeleteAsync(
                httpContext,
                cancellationToken);

        httpContext.Response.StatusCode = response.StatusCode;
        httpContext.Response.Headers.CacheControl = "no-store";
        await httpContext.Response.WriteAsJsonAsync(
            response,
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Logs the classified response without sensitive request data.
    /// </summary>
    /// <param name="response">The classified error response.</param>
    /// <param name="exception">The handled exception.</param>
    private void LogException(
        ErrorResponse response,
        Exception exception)
    {

        if (exception is DependencyUnavailableException dependencyException)
        {
            ApiLogMessages.DependencyUnavailable(
                logger,
                GetDependencyName(dependencyException),
                dependencyException);

            return;
        }

        if (response.StatusCode == StatusCodes.Status500InternalServerError)
        {
            ApiLogMessages.UnhandledException(
                logger,
                exception);

            return;
        }

        ApiLogMessages.ExpectedHttpError(
            logger,
            response.StatusCode,
            response.ErrorCode);
    }

    /// <summary>
    /// Returns only the bounded exception type used to classify a dependency outage.
    /// </summary>
    /// <param name="exception">The dependency exception.</param>
    /// <returns>The dependency exception type name.</returns>
    private static string GetDependencyName(DependencyUnavailableException exception)
    {

        return exception.InnerException?.GetType().Name ?? exception.GetType().Name;
    }
}
