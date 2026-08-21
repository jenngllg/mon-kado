using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines persistence operations for authentication sessions.
/// </summary>
public interface IAuthenticationSessionRepository
{
    /// <summary>
    /// Adds a session to the current unit of work.
    /// </summary>
    /// <param name="session">The session to add.</param>
    void Add(AuthenticationSession session);

    /// <summary>
    /// Gets an untracked session by identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session when found; otherwise, <see langword="null" />.</returns>
    Task<AuthenticationSession?> GetByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the stored session ticket and expiration.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="protectedTicket">The protected authentication ticket.</param>
    /// <param name="renewedAt">The UTC renewal date and time.</param>
    /// <param name="expiresAt">The UTC expiration date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task UpdateAsync(
        Guid sessionId,
        Guid userId,
        byte[] protectedTicket,
        DateTime renewedAt,
        DateTime expiresAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a session by identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DeleteAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one batch of expired sessions.
    /// </summary>
    /// <param name="cutoff">The inclusive UTC expiration cutoff.</param>
    /// <param name="batchSize">The maximum number of sessions to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted sessions.</returns>
    Task<int> DeleteExpiredAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
