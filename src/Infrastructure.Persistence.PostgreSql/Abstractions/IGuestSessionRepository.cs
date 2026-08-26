using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL persistence operations for guest sessions.
/// </summary>
public interface IGuestSessionRepository
{
    /// <summary>Adds a guest session to the current unit of work.</summary>
    /// <param name="session">The guest session.</param>
    void Add(GuestSession session);

    /// <summary>Gets a guest session without tracking it.</summary>
    /// <param name="sessionId">The guest session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The guest session when found.</returns>
    Task<GuestSession?> GetByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    /// <summary>Deletes one batch of expired guest sessions.</summary>
    /// <param name="expiresBefore">The inclusive expiration threshold.</param>
    /// <param name="batchSize">The maximum number of rows to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted sessions.</returns>
    Task<int> DeleteExpiredAsync(
        DateTime expiresBefore,
        int batchSize,
        CancellationToken cancellationToken);
}
