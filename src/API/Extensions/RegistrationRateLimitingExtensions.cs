using System.Globalization;
using System.Threading.RateLimiting;
using JennGllg.Fr.MonKado.Back.Api.Errors;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class RegistrationRateLimitingExtensions
{
    public const string RegistrationPolicy = "AccountRegistration";

    private const int PermitLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddRegistrationRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RegistrationPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = PermitLimit,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        Window = Window
                    }));

            options.OnRejected = async (rejectionContext, cancellationToken) =>
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
                        "Too many account registration attempts. Retry later.",
                        "RATE_LIMIT_EXCEEDED")
                    .ExecuteAsync(context);
            };
        });

        return services;
    }
}
