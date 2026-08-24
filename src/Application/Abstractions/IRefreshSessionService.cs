using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Proves existing refresh sessions and creates refresh-only MonKado sessions.
/// </summary>
public interface IRefreshSessionService
{
    /// <summary>
    /// Proves possession of an active refresh session and returns its identifier.
    /// </summary>
    /// <param name="refreshToken">The optional refresh token held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The proven session identifier, or <see langword="null" /> when no active session is proven.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<Guid?> ProveCurrentSessionAsync(
        string? refreshToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates refresh-only session material in the caller's current transaction.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="isPersistent">Whether the browser session is persistent.</param>
    /// <param name="requestedSessionId">The optional one-time identifier that must become the session identifier.</param>
    /// <param name="currentSessionId">The optional browser session selected for replacement by the caller's proof or containment policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created refresh-only session material.</returns>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<AccountRefreshSession> CreateAsync(
        Guid memberId,
        bool isPersistent,
        Guid? requestedSessionId,
        Guid? currentSessionId,
        CancellationToken cancellationToken);
}
