using JennGllg.Fr.MonKado.Back.Api.Abstractions;

using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>Serializes native image allocations within the small VPS memory budget.</summary>
/// <param name="timeProvider">The clock controlling the bounded admission wait.</param>
public class ImageProcessingLimiter(TimeProvider timeProvider) : IImageProcessingLimiter, IDisposable
{
    private const int MaximumQueuedUploads = 2;
    private static readonly TimeSpan _maximumWait = TimeSpan.FromSeconds(5);
    private readonly ConcurrencyLimiter _limiter = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = 1,
        QueueLimit = MaximumQueuedUploads,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });

    /// <inheritdoc />
    public async ValueTask<RateLimitLease?> AcquireAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            _maximumWait,
            timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {

            return await _limiter.AcquireAsync(
                permitCount: 1,
                linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {

            return null;
        }
    }

    /// <summary>Releases queued callers when the API host stops.</summary>
    public void Dispose()
    {
        _limiter.Dispose();
        GC.SuppressFinalize(this);
    }
}
