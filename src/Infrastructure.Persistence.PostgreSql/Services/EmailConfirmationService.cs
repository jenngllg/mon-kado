using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using System.Diagnostics;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

internal class EmailConfirmationService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IAuthenticationEmailOutboxRepository outboxRepository,
    UserManager<MonKadoUser> userManager,
    ILookupNormalizer lookupNormalizer,
    TimeProvider timeProvider) : IEmailConfirmationService
{
    private static readonly TimeSpan _minimumRequestResponseDuration =
        TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan _minimumRequestInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _requestWindow = TimeSpan.FromHours(1);
    private const int MaximumRequestsPerWindow = 5;
    private static readonly UTF8Encoding _strictUtf8 = new(
        false,
        true);
    /// <summary>
    /// Executes the confirm async operation.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="token">The token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task<bool> ConfirmAsync(
        string userId,
        string token,
        CancellationToken cancellationToken)
    {

        if (!Guid.TryParseExact(
            userId,
            "D",
            out var parsedUserId) ||
            parsedUserId == Guid.Empty ||
            !TryDecodeToken(
                token,
                out var decodedToken))
        {

            return false;
        }

        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(async () =>
                await ConfirmOnceAsync(
                    parsedUserId,
                    decodedToken,
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
    /// Executes the request async operation.
    /// </summary>
    /// <param name="email">The email.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task RequestAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var normalizedEmail = lookupNormalizer.NormalizeEmail(email);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return;

        try
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();
            await executionStrategy.ExecuteAsync(async () =>
                await RequestOnceAsync(
                    normalizedEmail,
                    cancellationToken));
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

    private async Task<bool> ConfirmOnceAsync(
        Guid userId,
        string decodedToken,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();

        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await userRepository.GetByIdForUpdateAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (IsExpiredUnconfirmedAccount(
            user,
            now))
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var tokenIsValid = await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<MonKadoUser>.ConfirmEmailTokenPurpose,
            decodedToken);

        if (!tokenIsValid)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        if (user.EmailConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);

            return true;
        }

        user.UnconfirmedAccountExpiresAt = null;
        user.Version++;
        var result = await userManager.ConfirmEmailAsync(
            user,
            decodedToken);

        return await CompleteConfirmationAsync(
            result,
            user.Id,
            now,
            outboxRepository,
            transaction,
            cancellationToken);
    }

    internal static async Task<bool> CompleteConfirmationAsync(
        IdentityResult result,
        Guid userId,
        DateTime confirmedAt,
        IAuthenticationEmailOutboxRepository outboxRepository,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {

        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        await outboxRepository.MarkPendingConfirmationMessagesProcessedAsync(
            userId,
            confirmedAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    internal static bool IsExpiredUnconfirmedAccount(
        MonKadoUser user,
        DateTime now)
    {

        return !user.EmailConfirmed &&
            (user.UnconfirmedAccountExpiresAt is not { } expiresAt ||
             expiresAt <= now);
    }

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
        var accountIsEligible =
            user is { EmailConfirmed: false, UnconfirmedAccountExpiresAt: { } expiration } &&
            expiration > now;
        var quotaUserId = user?.Id ?? Guid.Empty;

        var pendingRequestExists = await outboxRepository.HasPendingConfirmationMessageAsync(
            quotaUserId,
            cancellationToken);

        var windowStart = now.Subtract(_requestWindow);
        var statistics = await outboxRepository.GetConfirmationRequestStatisticsAsync(
            quotaUserId,
            windowStart,
            cancellationToken);

        var accountQuotaAllowsRequest = statistics is null ||
            (statistics.Count < MaximumRequestsPerWindow &&
             statistics.LatestRequestAt <= now.Subtract(_minimumRequestInterval));

        if (accountIsEligible && !pendingRequestExists && accountQuotaAllowsRequest)
        {
            outboxRepository.Add(
                AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
                    user!.Id,
                    now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static bool TryDecodeToken(
        string token,
        out string decodedToken)
    {
        decodedToken = string.Empty;
        try
        {
            var base64 = token.Replace(
                '-',
                '+').Replace(
                    '_',
                    '/');
            var remainder = base64.Length % 4;

            if (remainder == 1)
                return false;

            if (remainder > 0)
                base64 = base64.PadRight(
                    base64.Length + 4 - remainder,
                    '=');

            decodedToken = _strictUtf8.GetString(Convert.FromBase64String(base64));

            return decodedToken.Length > 0;
        }
        catch (FormatException)
        {

            return false;
        }
        catch (DecoderFallbackException)
        {

            return false;
        }
    }

    private static async Task DelayUntilMinimumResponseDurationAsync(
        long startedAt,
        CancellationToken cancellationToken)
    {
        var remaining =
            _minimumRequestResponseDuration - Stopwatch.GetElapsedTime(startedAt);

        if (remaining > TimeSpan.Zero)
            await Task.Delay(
                remaining,
                cancellationToken);
    }

}
