using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Deletes expired guest credentials while retaining wishlist participants.
/// </summary>
/// <param name="repository">The guest-session repository.</param>
/// <param name="timeProvider">The time provider.</param>
public class ExpiredGuestSessionCleanup(
    IGuestSessionRepository repository,
    TimeProvider timeProvider) : IExpiredGuestSessionCleanup
{
    /// <inheritdoc />
    public async Task<int> DeleteExpiredSessionsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        try
        {
            return await repository.DeleteExpiredAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                batchSize,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
