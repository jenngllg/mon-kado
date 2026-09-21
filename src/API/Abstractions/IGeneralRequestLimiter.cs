using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>Owns the shared process-local perimeter partitions.</summary>
public interface IGeneralRequestLimiter
{
    /// <summary>Attempts immediate admission without queuing or authenticating the caller.</summary>
    /// <param name="context">The request with its validated proxy address.</param>
    /// <returns>A lease that must be disposed by the caller.</returns>
    RateLimitLease AttemptAcquire(HttpContext context);
}
