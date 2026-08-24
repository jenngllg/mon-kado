using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Removes processed authentication emails from persistence.
/// </summary>
/// <param name="emailOutboxRepository">The authentication email outbox repository.</param>
public class ProcessedAuthenticationEmailCleanup(
    IAuthenticationEmailOutboxRepository emailOutboxRepository)
    : IProcessedAuthenticationEmailCleanup
{
    /// <inheritdoc />
    public async Task<int> DeleteProcessedEmailsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        return await emailOutboxRepository.DeleteProcessedAsync(
            cutoff,
            batchSize,
            cancellationToken);
    }
}
