using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

internal class AuthenticationEmailDispatcher(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IAuthenticationEmailOutboxRepository outboxRepository,
    UserManager<MonKadoUser> userManager,
    IAuthenticationEmailSender sender,
    TimeProvider timeProvider) : IAuthenticationEmailDispatcher
{
    private static readonly TimeSpan _maximumRetryDelay = TimeSpan.FromHours(24);
    /// <summary>
    /// Executes the dispatch pending async operation.
    /// </summary>
    /// <param name="frontendOrigin">The frontend origin.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task<int> DispatchPendingAsync(
        Uri frontendOrigin,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frontendOrigin);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            leaseDuration,
            TimeSpan.Zero);

        var claimedCount = 0;
        while (claimedCount < batchSize)
        {
            var messageId = await ClaimPendingMessageAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                leaseDuration,
                cancellationToken);

            if (messageId is null)
                break;

            await DeliverMessageAsync(
                messageId.Value,
                frontendOrigin,
                cancellationToken);
            claimedCount++;
        }

        return claimedCount;
    }

    private async Task<Guid?> ClaimPendingMessageAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var executionStrategy = context.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
            await ClaimPendingMessageOnceAsync(
                now,
                leaseDuration,
                cancellationToken));
    }

    private async Task<Guid?> ClaimPendingMessageOnceAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        var message = await outboxRepository.GetNextForUpdateAsync(
            now,
            cancellationToken);

        if (message is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        message.Claim(now.Add(leaseDuration));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return message.Id;
    }

    internal async Task DeliverMessageAsync(
        Guid messageId,
        Uri frontendOrigin,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var message = await outboxRepository.GetByIdForUpdateAsync(
            messageId,
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!CanDeliver(
            message,
            now))
            return;

        var deliverableMessage = message!;

        var user = await userRepository.Query()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == deliverableMessage.UserId,
                cancellationToken);

        if (!CanReceiveConfirmation(
            user,
            now))
        {
            deliverableMessage.MarkProcessed(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return;
        }

        var eligibleUser = user!;
        var token = await userManager.GenerateEmailConfirmationTokenAsync(eligibleUser);
        var confirmationUrl = BuildConfirmationUrl(
            frontendOrigin,
            eligibleUser.Id,
            token);
        try
        {
            var result = await sender.SendEmailConfirmationAsync(
                new AuthenticationEmailMessage(
                    deliverableMessage.Id,
                    eligibleUser.Email!,
                    confirmationUrl),
                cancellationToken);
            deliverableMessage.MarkProcessed(
                timeProvider.GetUtcNow().UtcDateTime,
                result.ProviderMessageId);
        }
        catch (AuthenticationEmailDeliveryException exception)
        {
            var failedAt = timeProvider.GetUtcNow().UtcDateTime;
            var retryDelay = GetRetryDelay(
                deliverableMessage.AttemptCount,
                exception.Category,
                exception.RetryAfter);
            deliverableMessage.ScheduleRetry(
                failedAt.Add(retryDelay),
                exception.Category.ToString().ToUpperInvariant());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    internal static bool CanReceiveConfirmation(
        MonKadoUser? user,
        DateTime now)
    {

        return user is
        {
            EmailConfirmed: false,
            Email: not null,
            UnconfirmedAccountExpiresAt: { } expiration
        } && expiration > now;
    }

    internal static bool CanDeliver(
        AuthenticationEmailOutboxMessage? message,
        DateTime now)
    {

        return message is
        {
            ProcessedAt: null,
            LockedUntil: { } lockedUntil
        } && lockedUntil > now;
    }

    private static Uri BuildConfirmationUrl(
        Uri frontendOrigin,
        Guid userId,
        string token)
    {
        var encodedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(token))
            .TrimEnd('=')
            .Replace(
                '+',
                '-')
            .Replace(
                '/',
                '_');
        var origin = frontendOrigin.GetLeftPart(UriPartial.Authority);

        return new Uri(
            $"{origin}/confirm-email#userId={userId:D}&token={encodedToken}",
            UriKind.Absolute);
    }

    internal static TimeSpan GetRetryDelay(
        int attemptCount,
        AuthenticationEmailFailureCategory category,
        TimeSpan? providerRetryAfter)
    {
        var slowRetry = category is
            AuthenticationEmailFailureCategory.Authentication or
            AuthenticationEmailFailureCategory.Permission or
            AuthenticationEmailFailureCategory.InvalidRequest or
            AuthenticationEmailFailureCategory.Unknown;
        var configuredDelay = slowRetry
            ? TimeSpan.FromHours(6)
            : attemptCount switch
            {
                <= 1 => TimeSpan.FromMinutes(1),
                2 => TimeSpan.FromMinutes(5),
                3 => TimeSpan.FromMinutes(15),
                4 => TimeSpan.FromHours(1),
                _ => TimeSpan.FromHours(6)
            };
        var requestedDelay = providerRetryAfter is { } retryAfter && retryAfter > configuredDelay
            ? retryAfter
            : configuredDelay;

        return requestedDelay > _maximumRetryDelay ? _maximumRetryDelay : requestedDelay;
    }
}
