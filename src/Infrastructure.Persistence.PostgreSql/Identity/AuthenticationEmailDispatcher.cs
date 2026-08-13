using System.Text;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal sealed class AuthenticationEmailDispatcher(
    MonKadoDbContext context,
    UserManager<MonKadoUser> userManager,
    IAuthenticationEmailSender sender,
    TimeProvider timeProvider) : IAuthenticationEmailDispatcher
{
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromHours(24);

    public async Task<int> DispatchPendingAsync(
        Uri frontendOrigin,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frontendOrigin);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        int claimedCount = 0;
        while (claimedCount < batchSize)
        {
            Guid? messageId = await ClaimPendingMessage(
                timeProvider.GetUtcNow(),
                leaseDuration,
                cancellationToken);
            if (messageId is null)
            {
                break;
            }

            await DeliverMessage(messageId.Value, frontendOrigin, cancellationToken);
            claimedCount++;
        }

        return claimedCount;
    }

    private async Task<Guid?> ClaimPendingMessage(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        IExecutionStrategy executionStrategy = context.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
            await ClaimPendingMessageOnce(now, leaseDuration, cancellationToken));
    }

    private async Task<Guid?> ClaimPendingMessageOnce(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        AuthenticationEmailOutboxMessage? message = await context.AuthenticationEmailOutboxMessages
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM public.authentication_email_outbox
                WHERE processed_at IS NULL
                  AND available_at <= {now}
                  AND (locked_until IS NULL OR locked_until <= {now})
                ORDER BY available_at, created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (message is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        message.Claim(now.Add(leaseDuration));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return message.Id;
    }

    private async Task DeliverMessage(
        Guid messageId,
        Uri frontendOrigin,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        AuthenticationEmailOutboxMessage? message = await context.AuthenticationEmailOutboxMessages
            .SingleOrDefaultAsync(candidate => candidate.Id == messageId, cancellationToken);
        if (message is null || message.ProcessedAt is not null)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (message.LockedUntil is null || message.LockedUntil <= now)
        {
            return;
        }

        MonKadoUser? user = await context.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == message.UserId,
            cancellationToken);
        if (user is not { EmailConfirmed: false, Email: not null } ||
            user.UnconfirmedAccountExpiresAt is null ||
            user.UnconfirmedAccountExpiresAt <= now)
        {
            message.MarkProcessed(now);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        string token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        Uri confirmationUrl = BuildConfirmationUrl(frontendOrigin, user.Id, token);
        try
        {
            AuthenticationEmailSendResult result = await sender.SendEmailConfirmationAsync(
                new AuthenticationEmailMessage(message.Id, user.Email, confirmationUrl),
                cancellationToken);
            message.MarkProcessed(timeProvider.GetUtcNow(), result.ProviderMessageId);
        }
        catch (AuthenticationEmailDeliveryException exception)
        {
            DateTimeOffset failedAt = timeProvider.GetUtcNow();
            TimeSpan retryDelay = GetRetryDelay(
                message.AttemptCount,
                exception.Category,
                exception.RetryAfter);
            message.ScheduleRetry(
                failedAt.Add(retryDelay),
                exception.Category.ToString().ToUpperInvariant());
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static Uri BuildConfirmationUrl(Uri frontendOrigin, Guid userId, string token)
    {
        string encodedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(token))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        string origin = frontendOrigin.GetLeftPart(UriPartial.Authority);
        return new Uri(
            $"{origin}/confirm-email#userId={userId:D}&token={encodedToken}",
            UriKind.Absolute);
    }

    private static TimeSpan GetRetryDelay(
        int attemptCount,
        AuthenticationEmailFailureCategory category,
        TimeSpan? providerRetryAfter)
    {
        bool slowRetry = category is
            AuthenticationEmailFailureCategory.Authentication or
            AuthenticationEmailFailureCategory.Permission or
            AuthenticationEmailFailureCategory.InvalidRequest or
            AuthenticationEmailFailureCategory.Unknown;
        TimeSpan configuredDelay = slowRetry
            ? TimeSpan.FromHours(6)
            : attemptCount switch
            {
                <= 1 => TimeSpan.FromMinutes(1),
                2 => TimeSpan.FromMinutes(5),
                3 => TimeSpan.FromMinutes(15),
                4 => TimeSpan.FromHours(1),
                _ => TimeSpan.FromHours(6)
            };
        TimeSpan requestedDelay = providerRetryAfter is { } retryAfter && retryAfter > configuredDelay
            ? retryAfter
            : configuredDelay;
        return requestedDelay > MaximumRetryDelay ? MaximumRetryDelay : requestedDelay;
    }
}
