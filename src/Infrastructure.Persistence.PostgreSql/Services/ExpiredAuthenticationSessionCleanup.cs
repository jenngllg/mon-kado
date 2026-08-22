using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Removes expired authentication sessions from persistence.
/// </summary>
/// <param name="sessionRepository">The authentication session repository.</param>
public class ExpiredAuthenticationSessionCleanup(
    IAuthenticationSessionRepository sessionRepository)
    : IExpiredAuthenticationSessionCleanup
{
    /// <summary>
    /// Deletes expired authentication sessions in a bounded batch.
    /// </summary>
    /// <param name="cutoff">The cutoff.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted sessions.</returns>
    public async Task<int> DeleteExpiredSessionsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        return await sessionRepository.DeleteExpiredAsync(
            cutoff,
            batchSize,
            cancellationToken);
    }
}
