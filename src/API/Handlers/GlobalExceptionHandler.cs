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

public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IRefreshTokenCookieService refreshTokenCookieService) : IExceptionHandler
{
    /// <summary>
    /// Executes the try handle async operation.
    /// </summary>
    /// <param name="httpContext">The http context.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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
            EmailConfirmationInvalidException => new ErrorResponse(
                StatusCodes.Status400BadRequest,
                "Email confirmation failed",
                "The email confirmation link is invalid or expired.",
                ErrorCodes.AccountEmailConfirmationInvalid,
                null),
            RequestValidationException validationException => new ErrorResponse(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "One or more fields are invalid.",
                ErrorCodes.RequestValidationError,
                validationException.ValidationErrors),
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

        httpContext.Response.StatusCode = response.StatusCode;
        httpContext.Response.Headers.CacheControl = "no-store";
        await httpContext.Response.WriteAsJsonAsync(
            response,
            cancellationToken);

        return true;
    }

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
            response.ErrorCode!);
    }

    internal static string GetDependencyName(DependencyUnavailableException exception)
    {

        return exception.InnerException?.GetType().Name ?? exception.GetType().Name;
    }
}
