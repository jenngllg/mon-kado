using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Defines the bounded operational policy used to deliver authentication e-mails.
/// </summary>
/// <param name="batchSize">The maximum number of messages dispatched in one cycle.</param>
/// <param name="leaseDuration">The duration of one message claim.</param>
/// <param name="maximumAttempts">The maximum number of provider calls for one message.</param>
/// <param name="firstRetryDelay">The delay after the first transient failure.</param>
/// <param name="secondRetryDelay">The delay after the second transient failure.</param>
/// <param name="thirdRetryDelay">The delay after the third transient failure.</param>
/// <param name="fourthRetryDelay">The delay after the fourth transient failure.</param>
/// <param name="subsequentRetryDelay">The delay after subsequent transient failures.</param>
/// <param name="slowRetryDelay">The delay after a non-transient provider rejection.</param>
/// <param name="maximumRetryDelay">The maximum accepted provider retry delay.</param>
[ExcludeFromCodeCoverage]
public class AuthenticationEmailDeliveryPolicy(
    int batchSize,
    TimeSpan leaseDuration,
    int maximumAttempts,
    TimeSpan firstRetryDelay,
    TimeSpan secondRetryDelay,
    TimeSpan thirdRetryDelay,
    TimeSpan fourthRetryDelay,
    TimeSpan subsequentRetryDelay,
    TimeSpan slowRetryDelay,
    TimeSpan maximumRetryDelay)
{
    /// <summary>
    /// Gets the maximum number of messages dispatched in one cycle.
    /// </summary>
    public int BatchSize { get; } = batchSize;

    /// <summary>
    /// Gets the duration of one message claim.
    /// </summary>
    public TimeSpan LeaseDuration { get; } = leaseDuration;

    /// <summary>
    /// Gets the maximum number of provider calls for one message.
    /// </summary>
    public int MaximumAttempts { get; } = maximumAttempts;

    /// <summary>
    /// Gets the delay after the first transient failure.
    /// </summary>
    public TimeSpan FirstRetryDelay { get; } = firstRetryDelay;

    /// <summary>
    /// Gets the delay after the second transient failure.
    /// </summary>
    public TimeSpan SecondRetryDelay { get; } = secondRetryDelay;

    /// <summary>
    /// Gets the delay after the third transient failure.
    /// </summary>
    public TimeSpan ThirdRetryDelay { get; } = thirdRetryDelay;

    /// <summary>
    /// Gets the delay after the fourth transient failure.
    /// </summary>
    public TimeSpan FourthRetryDelay { get; } = fourthRetryDelay;

    /// <summary>
    /// Gets the delay after subsequent transient failures.
    /// </summary>
    public TimeSpan SubsequentRetryDelay { get; } = subsequentRetryDelay;

    /// <summary>
    /// Gets the delay after a non-transient provider rejection.
    /// </summary>
    public TimeSpan SlowRetryDelay { get; } = slowRetryDelay;

    /// <summary>
    /// Gets the maximum accepted provider retry delay.
    /// </summary>
    public TimeSpan MaximumRetryDelay { get; } = maximumRetryDelay;
}
