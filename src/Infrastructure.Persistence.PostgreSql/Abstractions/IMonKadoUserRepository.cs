using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines persistence operations for MonKado users.
/// </summary>
public interface IMonKadoUserRepository
{
    /// <summary>
    /// Returns an untracked query for users.
    /// </summary>
    /// <returns>An untracked user query.</returns>
    IQueryable<MonKadoUser> Query();

    /// <summary>
    /// Gets a tracked user by identifier for an update operation.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked user when found; otherwise, <see langword="null" />.</returns>
    Task<MonKadoUser?> GetByIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets and locks a tracked user by identifier and normalized email address.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="normalizedEmail">The normalized email address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The locked user when found; otherwise, <see langword="null" />.</returns>
    Task<MonKadoUser?> GetByIdForUpdateAsync(
        Guid userId,
        string normalizedEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets and locks a tracked user by normalized email address.
    /// </summary>
    /// <param name="normalizedEmail">The normalized email address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The locked user when found; otherwise, <see langword="null" />.</returns>
    Task<MonKadoUser?> GetByNormalizedEmailForUpdateAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one batch of expired unconfirmed users.
    /// </summary>
    /// <param name="cutoff">The inclusive UTC expiration cutoff.</param>
    /// <param name="batchSize">The maximum number of users to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted users.</returns>
    Task<int> DeleteExpiredUnconfirmedAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
