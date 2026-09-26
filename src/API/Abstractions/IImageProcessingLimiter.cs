using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>Shares bounded admission across gift, profile and merchant-preview image processing.</summary>
public interface IImageProcessingLimiter
{
    /// <summary>Waits briefly for exclusive admission before the upload body is read.</summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A disposable lease, or null when the maximum wait expires.</returns>
    /// <exception cref="OperationCanceledException">The caller cancels admission.</exception>
    ValueTask<RateLimitLease?> AcquireAsync(CancellationToken cancellationToken);
}
