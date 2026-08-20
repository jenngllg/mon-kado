using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Api.Errors;

internal sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        IResult problem;
        switch (exception)
        {
            case EmailNotConfirmedException:
                problem = ApiProblemDetails.Create(
                    httpContext,
                    StatusCodes.Status401Unauthorized,
                    "email-not-confirmed",
                    "Authentication failed",
                    "Confirm your email address before signing in.",
                    "EMAIL_NOT_CONFIRMED");
                break;

            case InvalidCredentialsException:
                problem = ApiProblemDetails.Create(
                    httpContext,
                    StatusCodes.Status401Unauthorized,
                    "invalid-credentials",
                    "Authentication failed",
                    "The email address or password is invalid.",
                    "INVALID_CREDENTIALS");
                break;

            case EmailConfirmationInvalidException:
                problem = ApiProblemDetails.Create(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    "email-confirmation-invalid",
                    "Email confirmation failed",
                    "The email confirmation link is invalid or expired.",
                    "EMAIL_CONFIRMATION_INVALID");
                break;

            case RequestValidationException validationException:
                problem = ApiProblemDetails.Create(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    "validation-error",
                    "Validation failed",
                    "One or more fields are invalid.",
                    "VALIDATION_ERROR",
                    validationException.Errors);
                break;

            case DependencyUnavailableException dependencyException:
                LogDependencyUnavailable(
                    dependencyException.InnerException?.GetType().Name ?? dependencyException.GetType().Name);
                problem = ApiProblemDetails.Create(
                    httpContext,
                    StatusCodes.Status503ServiceUnavailable,
                    "dependency-unavailable",
                    "Service temporarily unavailable",
                    "A required service is temporarily unavailable. Retry later.",
                    "DEPENDENCY_UNAVAILABLE");
                break;

            default:
                LogUnhandledException(exception);
                problem = ApiProblemDetails.Create(
                    httpContext,
                    StatusCodes.Status500InternalServerError,
                    "internal-server-error",
                    "Internal server error",
                    "An unexpected error occurred.",
                    "INTERNAL_SERVER_ERROR");
                break;
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        await problem.ExecuteAsync(httpContext);
        return true;
    }

    [LoggerMessage(1000, LogLevel.Warning,
        "A request dependency was unavailable. Exception type: {ExceptionType}")]
    private partial void LogDependencyUnavailable(string exceptionType);

    [LoggerMessage(1001, LogLevel.Error,
        "An unhandled exception occurred while processing a request.")]
    private partial void LogUnhandledException(Exception exception);
}
