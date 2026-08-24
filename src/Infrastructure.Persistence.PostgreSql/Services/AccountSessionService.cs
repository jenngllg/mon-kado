using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql.EntityFrameworkCore.PostgreSQL;

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
/// <param name="refreshSessionService">The refresh session service.</param>
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
    IRefreshSessionService refreshSessionService,
    TimeProvider timeProvider) : IAccountSessionService
{
    private const int MaximumTransactionRetryCount = 3;
    private static readonly TimeSpan _maximumTransactionRetryDelay = TimeSpan.FromSeconds(1);

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
            var executionStrategy = CreateTransactionExecutionStrategy();
            var executionState = new AccountLoginExecutionState(
                Guid.CreateVersion7(timeProvider.GetUtcNow().UtcDateTime));
            var result = await executionStrategy.ExecuteInTransactionAsync(
                executionState,
                (
                    state,
                    operationCancellationToken) => ExecuteLoginAttemptAsync(
                        state,
                        user.Id,
                        user.NormalizedEmail,
                        password,
                        rememberMe,
                        currentRefreshToken,
                        operationCancellationToken),
                WasLoginOperationCommittedAsync,
                cancellationToken);

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
            var executionStrategy = CreateTransactionExecutionStrategy();
            var executionState = new RefreshSessionExecutionState(sessionId);

            return await executionStrategy.ExecuteInTransactionAsync(
                executionState,
                (
                    state,
                    operationCancellationToken) => ExecuteRefreshAttemptAsync(
                        state,
                        refreshToken,
                        operationCancellationToken),
                WasRefreshOperationCommittedAsync,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Clears prior attempt markers and stages one password-login transaction attempt.
    /// </summary>
    /// <param name="executionState">The password-login execution state.</param>
    /// <param name="userId">The member identifier.</param>
    /// <param name="normalizedEmail">The normalized email.</param>
    /// <param name="password">The password.</param>
    /// <param name="isPersistent">Whether the session has a fixed persistent lifetime.</param>
    /// <param name="currentRefreshToken">The refresh token currently held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authentication result and its tokens when successful.</returns>
    private async Task<AccountSessionLoginResult> ExecuteLoginAttemptAsync(
        AccountLoginExecutionState executionState,
        Guid userId,
        string normalizedEmail,
        string password,
        bool isPersistent,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        executionState.Reset();
        var result = await AuthenticateAndCreateSessionAsync(
            executionState,
            userId,
            normalizedEmail,
            password,
            isPersistent,
            currentRefreshToken,
            cancellationToken);

        if (result.Tokens is { } tokens)
            executionState.RecordSession(
                userId,
                tokens.RefreshToken);

        return result;
    }

    /// <summary>
    /// Authenticates an account and stages its session while holding the account lock.
    /// </summary>
    /// <param name="executionState">The password-login execution state.</param>
    /// <param name="userId">The member identifier.</param>
    /// <param name="normalizedEmail">The normalized email.</param>
    /// <param name="password">The password.</param>
    /// <param name="isPersistent">Whether the session has a fixed persistent lifetime.</param>
    /// <param name="currentRefreshToken">The refresh token currently held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authentication result and its tokens when successful.</returns>
    private async Task<AccountSessionLoginResult> AuthenticateAndCreateSessionAsync(
        AccountLoginExecutionState executionState,
        Guid userId,
        string normalizedEmail,
        string password,
        bool isPersistent,
        string? currentRefreshToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var user = await userRepository.GetByIdForUpdateAsync(
            userId,
            normalizedEmail,
            cancellationToken);

        if (user is null)
        {
            PerformTimingEqualizationHash(password);

            return new AccountSessionLoginResult(
                AccountLoginResult.InvalidCredentials,
                null);
        }

        var existingUser = user;

        if (await userManager.IsLockedOutAsync(existingUser))
        {
            PerformTimingEqualizationHash(password);

            return new AccountSessionLoginResult(
                AccountLoginResult.InvalidCredentials,
                null);
        }

        if (existingUser.PasswordHash is null)
        {
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
            executionState.RecordPasswordFailure();

            return new AccountSessionLoginResult(
                AccountLoginResult.InvalidCredentials,
                null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!existingUser.EmailConfirmed &&
            existingUser.UnconfirmedAccountExpiresAt is { } expiresAt &&
            expiresAt <= now)
        {
            return new AccountSessionLoginResult(
                AccountLoginResult.InvalidCredentials,
                null);
        }

        if (!existingUser.EmailConfirmed)
        {
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

        var currentSessionId = await refreshSessionService.ProveCurrentSessionAsync(
            currentRefreshToken,
            cancellationToken);
        var refreshSession = await refreshSessionService.CreateAsync(
            existingUser.Id,
            isPersistent,
            executionState.SessionId,
            currentSessionId,
            cancellationToken);
        var tokens = CreateTokens(
            existingUser.Id,
            refreshSession);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountSessionLoginResult(
            AccountLoginResult.Success,
            tokens);
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

        if (!refreshTokenService.Verify(
            refreshToken,
            session.RefreshTokenHash))
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        await RevokeSessionAndCommitAsync(
            session,
            timeProvider.GetUtcNow().UtcDateTime,
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
    /// Clears prior attempt markers and stages one refresh-session transaction attempt.
    /// </summary>
    /// <param name="executionState">The refresh-session execution state.</param>
    /// <param name="currentRefreshToken">The current refresh token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rotated tokens when the session remains valid; otherwise, <see langword="null" />.</returns>
    private async Task<AccountSessionTokens?> ExecuteRefreshAttemptAsync(
        RefreshSessionExecutionState executionState,
        string currentRefreshToken,
        CancellationToken cancellationToken)
    {
        executionState.Reset();

        return await RotateSessionAsync(
            executionState,
            currentRefreshToken,
            cancellationToken);
    }

    /// <summary>
    /// Rotates a refresh session while holding its database lock.
    /// </summary>
    /// <param name="executionState">The refresh-session execution state.</param>
    /// <param name="currentRefreshToken">The current refresh token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rotated tokens when the session remains valid; otherwise, <see langword="null" />.</returns>
    private async Task<AccountSessionTokens?> RotateSessionAsync(
        RefreshSessionExecutionState executionState,
        string currentRefreshToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var userId = await sessionRepository.GetUserIdAsync(
            executionState.SessionId,
            cancellationToken);

        if (userId is not { } existingUserId)
            return null;

        var user = await userRepository.GetByIdForUpdateAsync(
            existingUserId,
            cancellationToken);
        var session = await sessionRepository.GetByIdForUpdateAsync(
            executionState.SessionId,
            cancellationToken);

        if (session is null)
            return null;

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
            executionState.RecordRevocation();

            return null;
        }

        var rotatedRefreshToken = refreshTokenService.Create(session.Id);
        var expiresAt = RefreshSessionPolicy.GetRotatedExpiration(
            now,
            session.ExpiresAt,
            session.IsPersistent);
        session.Rotate(
            rotatedRefreshToken.Hash,
            now,
            expiresAt);
        var refreshSession = new AccountRefreshSession(
            rotatedRefreshToken.Value,
            expiresAt,
            session.IsPersistent);
        var tokens = CreateTokens(
            user.Id,
            refreshSession);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        executionState.RecordRotation(
            user.Id,
            tokens.RefreshToken,
            tokens.IsPersistent);

        return tokens;
    }

    /// <summary>
    /// Verifies the exact session or terminates an ambiguous failed-password attempt without replaying it.
    /// </summary>
    /// <param name="executionState">The password-login execution state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the exact session was committed or a failed-password attempt must not be replayed.</returns>
    private async Task<bool> WasLoginOperationCommittedAsync(
        AccountLoginExecutionState executionState,
        CancellationToken cancellationToken)
    {
        if (executionState.AttemptedSessionMemberId is { } memberId &&
            executionState.AttemptedRefreshToken is { } refreshToken)
        {
            var session = await context.AuthenticationSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    storedSession =>
                        storedSession.Id == executionState.SessionId &&
                        storedSession.UserId == memberId,
                    cancellationToken);

            if (session is null)
                return false;

            if (session.RevokedAt is null &&
                refreshTokenService.Verify(
                    refreshToken,
                    session.RefreshTokenHash))
                return true;

            executionState.PrepareSessionRetry(
                Guid.CreateVersion7(timeProvider.GetUtcNow().UtcDateTime));

            return false;
        }

        return executionState.PasswordFailureWasRecorded;
    }

    /// <summary>
    /// Verifies the exact rotation or a terminal revocation after an ambiguous refresh-session commit.
    /// </summary>
    /// <param name="executionState">The refresh-session execution state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the attempted refresh outcome is reflected in PostgreSQL.</returns>
    private async Task<bool> WasRefreshOperationCommittedAsync(
        RefreshSessionExecutionState executionState,
        CancellationToken cancellationToken)
    {
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                storedSession => storedSession.Id == executionState.SessionId,
                cancellationToken);

        if (executionState.AttemptedSessionMemberId is { } memberId &&
            executionState.AttemptedRefreshToken is { } refreshToken &&
            executionState.AttemptedIsPersistent is { } isPersistent)
            return session is
            {
                RevokedAt: null
            } &&
                session.UserId == memberId &&
                session.IsPersistent == isPersistent &&
                refreshTokenService.Verify(
                    refreshToken,
                    session.RefreshTokenHash);

        return executionState.RevocationWasRecorded &&
            (session is null || session.RevokedAt is not null);
    }

    /// <summary>
    /// Creates the access and refresh token response for a session.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="refreshSession">The refresh-only session material.</param>
    /// <returns>The session tokens.</returns>
    private AccountSessionTokens CreateTokens(
        Guid userId,
        AccountRefreshSession refreshSession)
    {

        return new AccountSessionTokens(
            accessTokenService.Create(userId),
            refreshSession.RefreshToken,
            refreshSession.RefreshTokenExpiresAt,
            refreshSession.IsPersistent);
    }

    /// <summary>
    /// Ensures that an Identity persistence mutation succeeded.
    /// </summary>
    /// <param name="result">The Identity result.</param>
    /// <param name="operation">The operation description used by the technical exception.</param>
    /// <exception cref="InvalidOperationException">The Identity mutation failed.</exception>
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
    /// Creates the retry strategy scoped exclusively to idempotent account-session transactions.
    /// </summary>
    /// <returns>The PostgreSQL retrying execution strategy.</returns>
    private NpgsqlRetryingExecutionStrategy CreateTransactionExecutionStrategy()
    {
        return new NpgsqlRetryingExecutionStrategy(
            context,
            MaximumTransactionRetryCount,
            _maximumTransactionRetryDelay,
            errorCodesToAdd: null);
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
