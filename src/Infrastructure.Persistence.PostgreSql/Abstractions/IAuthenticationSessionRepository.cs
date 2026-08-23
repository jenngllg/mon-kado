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
    /// <param name="session">The session.</param>
    void Add(AuthenticationSession session);

    /// <summary>
    /// Gets and locks a session for update.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session when found.</returns>
    Task<AuthenticationSession?> GetByIdForUpdateAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the member identifier associated with a session without tracking it.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The member identifier when the session exists; otherwise, <see langword="null" />.</returns>
    Task<Guid?> GetUserIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one batch of expired sessions.
    /// </summary>
    /// <param name="cutoff">The inclusive expiration cutoff.</param>
    /// <param name="batchSize">The maximum batch size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted sessions.</returns>
    Task<int> DeleteExpiredAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every active refresh session for a member.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="revokedAt">The revocation date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of revoked sessions.</returns>
    Task<int> RevokeAllForUserAsync(
        Guid userId,
        DateTime revokedAt,
        CancellationToken cancellationToken);
}
