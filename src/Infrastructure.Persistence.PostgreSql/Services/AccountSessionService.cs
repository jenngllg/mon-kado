using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Authenticates accounts and manages their refresh sessions.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="userRepository">The member repository.</param>
/// <param name="sessionRepository">The authentication session repository.</param>
/// <param name="userManager">The Identity user manager.</param>
/// <param name="passwordHasher">The password hasher.</param>
/// <param name="accessTokenService">The access token service.</param>
/// <param name="refreshTokenService">The refresh token service.</param>
/// <param name="timeProvider">The time provider.</param>
public class AccountSessionService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IAuthenticationSessionRepository sessionRepository,
    UserManager<MonKadoUser> userManager,
    IPasswordHasher<MonKadoUser> passwordHasher,
    IAccessTokenService accessTokenService,
    IRefreshTokenService refreshTokenService,
    TimeProvider timeProvider) : IAccountSessionService
{
    private static readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan _persistentSessionLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// Authenticates an account and creates an independent refresh session.
    /// </summary>
    /// <param name="email">The normalized account lookup email.</param>
    /// <param name="password">The password.</param>
    /// <param name="rememberMe">Whether the refresh session is persistent.</param>
    /// <param name="currentRefreshToken">The refresh token currently held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authentication result and its tokens when successful.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<AccountSessionLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var user = await userManager.FindByEmailAsync(email);

            if (user is null)
            {
                PerformTimingEqualizationHash(password);

                return new AccountSessionLoginResult(
                    AccountLoginResult.InvalidCredentials,
                    null);
            }

            ArgumentNullException.ThrowIfNull(user.NormalizedEmail);
            var executionStrategy = context.Database.CreateExecutionStrategy();
            var result = await executionStrategy.ExecuteAsync(() =>
                AuthenticateAndCreateSessionAsync(
                    user.Id,
                    user.NormalizedEmail,
                    password,
                    rememberMe,
                    currentRefreshToken,
                    cancellationToken));

            return result;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Revokes the refresh session held by the current browser.
    /// </summary>
    /// <param name="refreshToken">The refresh token currently held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task LogoutAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (refreshToken is null ||
            !refreshTokenService.TryGetSessionId(
                refreshToken,
                out var sessionId))
            return;

        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();
            await executionStrategy.ExecuteAsync(() => LogoutSessionAsync(
                sessionId,
                refreshToken,
                cancellationToken));
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Rotates an existing refresh session.
    /// </summary>
    /// <param name="refreshToken">The current refresh token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rotated tokens when the session is valid; otherwise, <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<AccountSessionTokens?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!refreshTokenService.TryGetSessionId(
            refreshToken,
            out var sessionId))
            return null;

        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(() =>
                RotateSessionAsync(
                    sessionId,
                    refreshToken,
                    cancellationToken));
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Authenticates an account and creates its session while holding the account lock.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="normalizedEmail">The normalized email.</param>
    /// <param name="password">The password.</param>
    /// <param name="isPersistent">Whether the session has a fixed persistent lifetime.</param>
    /// <param name="currentRefreshToken">The refresh token currently held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authentication result and its tokens when successful.</returns>
    private async Task<AccountSessionLoginResult> AuthenticateAndCreateSessionAsync(
        Guid userId,
        string normalizedEmail,
        string password,
        bool isPersistent,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await userRepository.GetByIdForUpdateAsync(
            userId,
            normalizedEmail,
            cancellationToken);

        if (user is null)
        {
            await transaction.CommitAsync(cancellationToken);
            PerformTimingEqualizationHash(password);

            return new AccountSessionLoginResult(
                AccountLoginResult.InvalidCredentials,
                null);
        }

        var existingUser = user;

        if (await userManager.IsLockedOutAsync(existingUser))
        {
            await transaction.CommitAsync(cancellationToken);
            PerformTimingEqualizationHash(password);

            return new AccountSessionLoginResult(
                AccountLoginResult.InvalidCredentials,
                null);
        }

        var passwordValid = await userManager.CheckPasswordAsync(
            existingUser,
            password);

        if (!passwordValid)
        {
            var failureResult = await userManager.AccessFailedAsync(existingUser);
            EnsureIdentityUpdateSucceeded(
                failureResult,
                "record the failed login attempt");
            await transaction.CommitAsync(cancellationToken);

            return new AccountSessionLoginResult(
                AccountLoginResult.InvalidCredentials,
                null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!existingUser.EmailConfirmed &&
            existingUser.UnconfirmedAccountExpiresAt is { } expiresAt &&
            expiresAt <= now)
        {
            await transaction.CommitAsync(cancellationToken);

            return new AccountSessionLoginResult(
                AccountLoginResult.InvalidCredentials,
                null);
        }

        if (!existingUser.EmailConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);

            return new AccountSessionLoginResult(
                AccountLoginResult.EmailNotConfirmed,
                null);
        }

        if (await userManager.GetAccessFailedCountAsync(existingUser) > 0)
        {
            var resetResult = await userManager.ResetAccessFailedCountAsync(existingUser);
            EnsureIdentityUpdateSucceeded(
                resetResult,
                "reset the failed login count");
        }

        var tokens = await CreateSessionAsync(
            existingUser.Id,
            isPersistent,
            currentRefreshToken,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AccountSessionLoginResult(
            AccountLoginResult.Success,
            tokens);
    }

    /// <summary>
    /// Creates and persists an authentication session in the current transaction.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="isPersistent">Whether the session has a fixed persistent lifetime.</param>
    /// <param name="currentRefreshToken">The refresh token currently held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created session tokens.</returns>
    private async Task<AccountSessionTokens> CreateSessionAsync(
        Guid userId,
        bool isPersistent,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await RevokeCurrentSessionAsync(
            currentRefreshToken,
            now,
            cancellationToken);

        var sessionId = Guid.CreateVersion7(now);
        var refreshToken = refreshTokenService.Create(sessionId);
        var expiresAt = now.Add(
            isPersistent
                ? _persistentSessionLifetime
                : _sessionLifetime);
        var session = AuthenticationSession.Create(
            sessionId,
            userId,
            refreshToken.Hash,
            isPersistent,
            now,
            expiresAt);
        var tokens = CreateTokens(
            userId,
            refreshToken.Value,
            expiresAt,
            isPersistent);

        sessionRepository.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return tokens;
    }

    /// <summary>
    /// Revokes a browser refresh session while holding its database lock.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="refreshToken">The refresh token held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task LogoutSessionAsync(
        Guid sessionId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var session = await sessionRepository.GetByIdForUpdateAsync(
            sessionId,
            cancellationToken);

        if (session is null || session.RevokedAt is not null)
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Preserve fixed-time verification before revoking both current and reused token variants.
        _ = refreshTokenService.Verify(
            refreshToken,
            session.RefreshTokenHash);

        await RevokeSessionAndCommitAsync(
            session,
            now,
            transaction,
            cancellationToken);
    }

    /// <summary>
    /// Revokes and commits an authentication session.
    /// </summary>
    /// <param name="session">The authentication session.</param>
    /// <param name="revokedAt">The revocation date.</param>
    /// <param name="transaction">The current database transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RevokeSessionAndCommitAsync(
        AuthenticationSession session,
        DateTime revokedAt,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        session.Revoke(revokedAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Rotates a refresh session while holding its database lock.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="currentRefreshToken">The current refresh token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rotated tokens when the session remains valid; otherwise, <see langword="null" />.</returns>
    private async Task<AccountSessionTokens?> RotateSessionAsync(
        Guid sessionId,
        string currentRefreshToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var userId = await sessionRepository.GetUserIdAsync(
            sessionId,
            cancellationToken);

        if (userId is not { } existingUserId)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        var user = await userRepository.GetByIdForUpdateAsync(
            existingUserId,
            cancellationToken);
        var session = await sessionRepository.GetByIdForUpdateAsync(
            sessionId,
            cancellationToken);

        if (session is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (user is null ||
            session.RevokedAt is not null ||
            session.ExpiresAt <= now ||
            !refreshTokenService.Verify(
                currentRefreshToken,
                session.RefreshTokenHash))
        {
            session.Revoke(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        var rotatedRefreshToken = refreshTokenService.Create(session.Id);
        var expiresAt = session.IsPersistent
            ? session.ExpiresAt
            : now.Add(_sessionLifetime);
        session.Rotate(
            rotatedRefreshToken.Hash,
            now,
            expiresAt);
        var tokens = CreateTokens(
            user.Id,
            rotatedRefreshToken.Value,
            expiresAt,
            session.IsPersistent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return tokens;
    }

    /// <summary>
    /// Revokes the previous refresh session held by the current browser.
    /// </summary>
    /// <param name="currentRefreshToken">The refresh token currently held by the browser.</param>
    /// <param name="revokedAt">The revocation date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RevokeCurrentSessionAsync(
        string? currentRefreshToken,
        DateTime revokedAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentRefreshToken) ||
            !refreshTokenService.TryGetSessionId(
                currentRefreshToken,
                out var sessionId))
            return;

        var session = await sessionRepository.GetByIdForUpdateAsync(
            sessionId,
            cancellationToken);

        if (session is null || session.RevokedAt is not null)
            return;

        session.Revoke(revokedAt);
    }

    /// <summary>
    /// Creates the access and refresh token response for a session.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="refreshTokenExpiresAt">The refresh token expiration.</param>
    /// <param name="isPersistent">Whether the session is persistent.</param>
    /// <returns>The session tokens.</returns>
    private AccountSessionTokens CreateTokens(
        Guid userId,
        string refreshToken,
        DateTime refreshTokenExpiresAt,
        bool isPersistent)
    {
        return new AccountSessionTokens(
            accessTokenService.Create(userId),
            refreshToken,
            refreshTokenExpiresAt,
            isPersistent);
    }

    private static void EnsureIdentityUpdateSucceeded(
        IdentityResult result,
        string operation)
    {
        if (result.Succeeded)
            return;

        var errorCodes = string.Join(
            ", ",
            result.Errors.Select(error => error.Code));

        throw new InvalidOperationException($"Unable to {operation}: {errorCodes}.");
    }

    /// <summary>
    /// Performs a dummy password hash to reduce account enumeration timing differences.
    /// </summary>
    /// <param name="password">The submitted password.</param>
    private void PerformTimingEqualizationHash(string password)
    {
        var dummyUser = new MonKadoUser
        {
            Id = Guid.Empty,
            UserName = "timing-equalization"
        };

        _ = passwordHasher.HashPassword(
            dummyUser,
            password);
    }
}
