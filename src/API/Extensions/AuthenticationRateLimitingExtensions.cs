using System.Globalization;
using System.Threading.RateLimiting;
using JennGllg.Fr.MonKado.Back.Api.Errors;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class AuthenticationRateLimitingExtensions
{
    public const string RegistrationPolicy = "AccountRegistration";
    public const string EmailConfirmationPolicy = "EmailConfirmation";
    public const string EmailConfirmationRequestPolicy = "EmailConfirmationRequest";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddAuthenticationRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RegistrationPolicy, context => CreateLimiter(context, 5));
            options.AddPolicy(EmailConfirmationPolicy, context => CreateLimiter(context, 10));
            options.AddPolicy(EmailConfirmationRequestPolicy, context => CreateLimiter(context, 5));

            options.OnRejected = async (rejectionContext, _) =>
            {
                HttpContext context = rejectionContext.HttpContext;
                TimeSpan retryAfter = Window;
                if (rejectionContext.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan metadata))
                {
                    retryAfter = metadata;
                }

                context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                context.Response.Headers.CacheControl = "no-store";

                await ApiProblemDetails.Create(
                        context,
                        StatusCodes.Status429TooManyRequests,
                        "rate-limit-exceeded",
                        "Rate limit exceeded",
                        "Too many authentication requests. Retry later.",
                        "RATE_LIMIT_EXCEEDED")
                    .ExecuteAsync(context);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> CreateLimiter(HttpContext context, int permitLimit)
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = Window
            });
    }
}
