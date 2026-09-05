using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL operations for obsolete gift-image deletions.
/// </summary>
public interface IGiftImageDeletionOutboxRepository
{
    /// <summary>
    /// Adds a deletion to the current unit of work.
    /// </summary>
    /// <param name="message">The deletion message.</param>
    void Add(GiftImageDeletionOutboxMessage message);

    /// <summary>
    /// Gets and locks the next available deletion.
    /// </summary>
    /// <param name="now">The current UTC date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked message, or <see langword="null" />.</returns>
    Task<GiftImageDeletionOutboxMessage?> GetNextForUpdateAsync(
        DateTime now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a tracked deletion by identifier.
    /// </summary>
    /// <param name="id">The deletion identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked message, or <see langword="null" />.</returns>
    Task<GiftImageDeletionOutboxMessage?> GetByIdForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a completed deletion from the current unit of work.
    /// </summary>
    /// <param name="message">The deletion message.</param>
    void Remove(GiftImageDeletionOutboxMessage message);
}
