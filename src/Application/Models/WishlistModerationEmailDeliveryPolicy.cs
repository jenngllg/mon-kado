namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains validated delivery, retry and retention bounds for moderation notifications.</summary>
/// <param name="batchSize">The maximum number of messages claimed per cycle.</param>
/// <param name="leaseDuration">The duration of each delivery claim.</param>
/// <param name="maximumAttempts">The maximum number of provider calls per message.</param>
/// <param name="retryDelays">The increasing retry delays; the last delay applies to subsequent attempts.</param>
/// <param name="maximumRetryDelay">The upper bound for provider Retry-After.</param>
/// <param name="processedRetention">The duration for retaining processed delivery records.</param>
public class WishlistModerationEmailDeliveryPolicy(
    int batchSize,
    TimeSpan leaseDuration,
    int maximumAttempts,
    IEnumerable<TimeSpan> retryDelays,
    TimeSpan maximumRetryDelay,
    TimeSpan processedRetention)
{
    private readonly TimeSpan[] _retryDelays = retryDelays.ToArray();
    /// <summary>Gets the maximum claims per cycle.</summary>
    public int BatchSize { get; } = batchSize;
    /// <summary>Gets the claim lifetime.</summary>
    public TimeSpan LeaseDuration { get; } = leaseDuration;
    /// <summary>Gets the maximum provider attempts.</summary>
    public int MaximumAttempts { get; } = maximumAttempts;
    /// <summary>Gets the duration for retaining terminal outbox records.</summary>
    public TimeSpan ProcessedRetention { get; } = processedRetention;

    /// <summary>Chooses a configured retry delay, respecting bounded provider guidance.</summary>
    /// <param name="attempt">The one-based attempt number.</param>
    /// <param name="retryAfter">The provider's optional retry delay.</param>
    /// <returns>The bounded delay until the next attempt.</returns>
    public TimeSpan GetRetryDelay(
        int attempt,
        TimeSpan? retryAfter)
    {
        var index = Math.Clamp(
            attempt - 1,
            0,
            _retryDelays.Length - 1);
        var delay = _retryDelays[index];

        if (retryAfter is TimeSpan requestedDelay && requestedDelay > delay)
            delay = requestedDelay;

        return delay > maximumRetryDelay ? maximumRetryDelay : delay;
    }
}
