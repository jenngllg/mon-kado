using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Requests and completes anonymous member password resets in PostgreSQL.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="userRepository">The member repository.</param>
/// <param name="sessionRepository">The authentication session repository.</param>
/// <param name="emailChangeRequestRepository">The member email change request repository.</param>
/// <param name="outboxRepository">The authentication email outbox repository.</param>
/// <param name="userManager">The Identity user manager.</param>
/// <param name="lookupNormalizer">The Identity lookup normalizer.</param>
/// <param name="timeProvider">The time provider.</param>
public class PasswordResetService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IAuthenticationSessionRepository sessionRepository,
    IMemberEmailChangeRequestRepository emailChangeRequestRepository,
    IAuthenticationEmailOutboxRepository outboxRepository,
    UserManager<MonKadoUser> userManager,
    ILookupNormalizer lookupNormalizer,
    TimeProvider timeProvider) : IPasswordResetService
{
    private static readonly TimeSpan _minimumRequestResponseDuration =
        TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan _minimumRequestInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _requestLifetime = TimeSpan.FromHours(1);
    private const int MaximumRequestsPerWindow = 5;
    /// <inheritdoc />
    public async Task RequestAsync(
        string email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = timeProvider.GetTimestamp();
        var normalizedEmail = lookupNormalizer.NormalizeEmail(email);
        ArgumentNullException.ThrowIfNull(normalizedEmail);

        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();
            await executionStrategy.ExecuteAsync(
                token => RequestOnceAsync(
                    normalizedEmail,
                    token),
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
        finally
        {
            await DelayUntilMinimumResponseDurationAsync(
                startedAt,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ResetAsync(
        string userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Guid.TryParseExact(
            userId,
            "D",
            out var parsedUserId) ||
            parsedUserId == Guid.Empty ||
            !AuthenticationEmailTokenEncoding.TryDecode(
                token,
                out var decodedToken))
            return false;

        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(
                currentToken => ResetOnceAsync(
                    parsedUserId,
                    decodedToken,
                    newPassword,
                    currentToken),
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
    /// Persists one enumeration-safe password reset request attempt.
    /// </summary>
    /// <param name="normalizedEmail">The normalized member email address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RequestOnceAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await userRepository.GetByNormalizedEmailForUpdateAsync(
            normalizedEmail,
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var quotaUserId = user?.Id ?? Guid.Empty;
        var expirationCutoff = now.Subtract(_requestLifetime);
        await outboxRepository.MarkExpiredPasswordResetMessagesProcessedAsync(
            quotaUserId,
            expirationCutoff,
            now,
            cancellationToken);
        var pendingRequestExists = await outboxRepository.HasPendingPasswordResetMessageAsync(
            quotaUserId,
            cancellationToken);
        var statistics = await outboxRepository.GetPasswordResetRequestStatisticsAsync(
            quotaUserId,
            expirationCutoff,
            cancellationToken);
        var accountQuotaAllowsRequest = statistics is null ||
            (statistics.Count < MaximumRequestsPerWindow &&
             statistics.LatestRequestAt <= now.Subtract(_minimumRequestInterval));

        if (user is
            {
                EmailConfirmed: true,
                Email: { } recipientEmail,
                SecurityStamp: { } securityStamp
            } &&
            !pendingRequestExists &&
            accountQuotaAllowsRequest)
        {
            outboxRepository.Add(AuthenticationEmailOutboxMessage.CreatePasswordReset(
                user.Id,
                recipientEmail,
                securityStamp,
                now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Resets one member password and invalidates its security state atomically.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="decodedToken">The decoded Identity reset token.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the password was reset; otherwise, <see langword="false" />.</returns>
    /// <exception cref="RequestValidationException">Thrown when Identity rejects the new password.</exception>
    /// <exception cref="PasswordResetInvalidException">Thrown when Identity detects a concurrent password reset.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Identity reports an unexpected failure.</exception>
    private async Task<bool> ResetOnceAsync(
        Guid userId,
        string decodedToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var member = await userRepository.GetByIdForUpdateAsync(
            userId,
            cancellationToken);

        if (member is null || !member.EmailConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var result = await userManager.ResetPasswordAsync(
            member,
            decodedToken,
            newPassword);

        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            return IdentityPasswordFailureTranslator.HandleResetFailure(result);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        member.AccessFailedCount = 0;
        member.LockoutEnd = null;
        var emailChangeRequest = await emailChangeRequestRepository
            .GetActiveByUserIdForUpdateAsync(
                member.Id,
                cancellationToken);
        Guid? revokedEmailChangeRequestId = null;

        if (emailChangeRequest is not null)
        {
            emailChangeRequest.Revoke(now);
            revokedEmailChangeRequestId = emailChangeRequest.Id;
        }

        await sessionRepository.RevokeAllForUserAsync(
            member.Id,
            now,
            cancellationToken);

        if (revokedEmailChangeRequestId is { } requestId)
            await outboxRepository.MarkPendingEmailChangeMessagesProcessedAsync(
                requestId,
                now,
                cancellationToken);

        await outboxRepository.MarkPendingPasswordResetMessagesProcessedAsync(
            member.Id,
            now,
            cancellationToken);
        ArgumentNullException.ThrowIfNull(member.Email);
        outboxRepository.Add(
            AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
                member.Id,
                member.Email,
                now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Delays an anonymous request until its minimum response duration is reached.
    /// </summary>
    /// <param name="startedAt">The request start timestamp.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task DelayUntilMinimumResponseDurationAsync(
        long startedAt,
        CancellationToken cancellationToken)
    {
        var remaining =
            _minimumRequestResponseDuration - timeProvider.GetElapsedTime(startedAt);

        if (remaining > TimeSpan.Zero)
            await Task.Delay(
                remaining,
                timeProvider,
                cancellationToken);
    }
}
