using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Claims and acknowledges recipient rows using durable lease fencing.</summary>
public interface IAccountErasureEmailRepository
{
    /// <summary>Atomically claims one eligible notification.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <param name="leaseDuration">The bounded lease duration.</param>
    /// <param name="maximumAttempts">The maximum durable attempt count.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The claim, or null when no eligible row exists.</returns>
    Task<AccountErasureEmailClaim?> ClaimAsync(
        DateTime now,
        TimeSpan leaseDuration,
        int maximumAttempts,
        CancellationToken cancellationToken);
    /// <summary>Atomically discards a terminal recipient and updates its audit, or schedules another attempt.</summary>
    /// <param name="claim">The fenced attempt.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="status">The accepted, failed or pending result.</param>
    /// <param name="retryAt">The next eligible attempt time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether this lease still owned and completed the operation.</returns>
    Task<bool> CompleteAsync(
        AccountErasureEmailClaim claim,
        DateTime now,
        AccountErasureNotificationStatus status,
        DateTime retryAt,
        CancellationToken cancellationToken);
}
