using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Removes expired unconfirmed member accounts.
/// </summary>
/// <param name="userRepository">The member repository.</param>
public class ExpiredAccountCleanup(IMonKadoUserRepository userRepository)
    : IExpiredAccountCleanup
{
    /// <summary>
    /// Executes the delete expired unconfirmed accounts async operation.
    /// </summary>
    /// <param name="cutoff">The cutoff.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task<int> DeleteExpiredUnconfirmedAccountsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        return await userRepository.DeleteExpiredUnconfirmedAsync(
            cutoff,
            batchSize,
            cancellationToken);
    }
}
