using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Logging;

using System.Globalization;
using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents authentication rate limiting extensions.
/// </summary>

public static class AuthenticationRateLimitingExtensions
{
    /// <summary>
    /// Identifies registration policy.
    /// </summary>
    public const string RegistrationPolicy = "AccountRegistration";
    /// <summary>
    /// Identifies email confirmation policy.
    /// </summary>
    public const string EmailConfirmationPolicy = "EmailConfirmation";
    /// <summary>
    /// Identifies login policy.
    /// </summary>
    public const string LoginPolicy = "Login";
    /// <summary>
    /// Identifies email confirmation request policy.
    /// </summary>
    public const string EmailConfirmationRequestPolicy = "EmailConfirmationRequest";

    private static readonly TimeSpan _window = TimeSpan.FromMinutes(1);
    /// <summary>
    /// Executes the add authentication rate limiting operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection AddAuthenticationRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(
                RegistrationPolicy,
                context => CreateLimiter(
                    context,
                    5));
            options.AddPolicy(
                LoginPolicy,
                context => CreateLimiter(
                    context,
                    10));
            options.AddPolicy(
                EmailConfirmationPolicy,
                context => CreateLimiter(
                    context,
                    10));
            options.AddPolicy(
                EmailConfirmationRequestPolicy,
                context => CreateLimiter(
                    context,
                    5));

            options.OnRejected = async (
                rejectionContext,
                cancellationToken) =>
            {
                var context = rejectionContext.HttpContext;
                var retryAfter = _window;

                if (rejectionContext.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var metadata))
                    retryAfter = metadata;

                context.Response.Headers.RetryAfter = Math.Max(
                    1,
                    (int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                context.Response.Headers.CacheControl = "no-store";

                var errorResponse = new ErrorResponse(
                    StatusCodes.Status429TooManyRequests,
                    "Rate limit exceeded",
                    "Too many authentication requests. Retry later.",
                    ErrorCodes.RequestRateLimitExceeded,
                    null);
                var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger(typeof(AuthenticationRateLimitingExtensions));
                ApiLogMessages.ExpectedHttpError(
                    logger,
                    errorResponse.StatusCode,
                    ErrorCodes.RequestRateLimitExceeded);

                context.Response.StatusCode = errorResponse.StatusCode;
                await context.Response.WriteAsJsonAsync(
                    errorResponse,
                    cancellationToken);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> CreateLimiter(
        HttpContext context,
        int permitLimit)
    {

        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = _window
            });
    }
}
