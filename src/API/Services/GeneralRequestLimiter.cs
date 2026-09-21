using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Options;

using Microsoft.Extensions.Options;

using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>Shares perimeter counters across requests and releases idle partitions.</summary>
public class GeneralRequestLimiter : IGeneralRequestLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    /// <summary>Creates the process-wide limiter with startup-validated settings.</summary>
    /// <param name="options">The fixed window and capacity settings.</param>
    public GeneralRequestLimiter(IOptions<GeneralRateLimitOptions> options)
    {
        var settings = options.Value;
        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var address = context.Connection.RemoteIpAddress;

            if (address?.IsIPv4MappedToIPv6 == true)
                address = address.MapToIPv4();

            return RateLimitPartition.GetFixedWindowLimiter(
                address?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
    }

    /// <inheritdoc />
    public RateLimitLease AttemptAcquire(HttpContext context)
    {

        return _limiter.AttemptAcquire(context);
    }

    /// <summary>Releases partition timers when the host stops.</summary>
    public void Dispose()
    {
        _limiter.Dispose();
        GC.SuppressFinalize(this);
    }
}
