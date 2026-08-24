using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Proves browser refresh sessions and creates refresh-only MonKado sessions.
/// </summary>
/// <param name="sessionRepository">The authentication session repository.</param>
/// <param name="refreshTokenService">The refresh token service.</param>
/// <param name="timeProvider">The time provider.</param>
public class RefreshSessionService(
    IAuthenticationSessionRepository sessionRepository,
    IRefreshTokenService refreshTokenService,
    TimeProvider timeProvider) : IRefreshSessionService
{
    /// <summary>
    /// Proves possession of an active refresh session and returns its identifier.
    /// </summary>
    /// <param name="refreshToken">The optional refresh token held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The proven session identifier, or <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public async Task<Guid?> ProveCurrentSessionAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (refreshToken is null ||
            !refreshTokenService.TryGetSessionId(
                refreshToken,
                out var sessionId))
            return null;

        try
        {
            var session = await sessionRepository.GetByIdAsync(
                sessionId,
                cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;

            if (session is null ||
                session.RevokedAt is not null ||
                session.ExpiresAt <= now ||
                !refreshTokenService.Verify(
                    refreshToken,
                    session.RefreshTokenHash))
                return null;

            return session.Id;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

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
    public async Task<AccountRefreshSession> CreateAsync(
        Guid memberId,
        bool isPersistent,
        Guid? requestedSessionId,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (currentSessionId is { } sessionId)
        {
            var currentSession = await sessionRepository.GetByIdForUpdateAsync(
                sessionId,
                cancellationToken);

            if (currentSession is { RevokedAt: null })
                currentSession.Revoke(now);
        }

        var newSessionId = requestedSessionId ?? Guid.CreateVersion7(now);
        var refreshToken = refreshTokenService.Create(newSessionId);
        var expiresAt = RefreshSessionPolicy.GetInitialExpiration(
            now,
            isPersistent);
        var session = AuthenticationSession.Create(
            newSessionId,
            memberId,
            refreshToken.Hash,
            isPersistent,
            now,
            expiresAt);
        sessionRepository.Add(session);

        return new AccountRefreshSession(
            refreshToken.Value,
            expiresAt,
            isPersistent);
    }
}
