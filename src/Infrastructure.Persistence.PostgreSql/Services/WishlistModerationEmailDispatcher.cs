using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Delivers durable moderation decisions in per-wishlist order without holding provider-spanning transactions.</summary>
/// <param name="repository">The leased outbox repository.</param>
/// <param name="sender">The typed provider sender.</param>
/// <param name="timeProvider">The UTC and cancellation clock.</param>
/// <param name="logger">The structured technical logger.</param>
public class WishlistModerationEmailDispatcher(
    IWishlistModerationEmailRepository repository,
    IWishlistModerationEmailSender sender,
    TimeProvider timeProvider,
    ILogger<WishlistModerationEmailDispatcher> logger) : IWishlistModerationEmailDispatcher
{
    /// <inheritdoc/>
    public async Task<int> DispatchAsync(
        WishlistModerationEmailDeliveryPolicy policy,
        CancellationToken cancellationToken)
    {
        await repository.PurgeAsync(
            timeProvider
                .GetUtcNow()
                .UtcDateTime.Subtract(policy.ProcessedRetention),
            policy.BatchSize,
            cancellationToken);
        var processed = 0;
        while (processed < policy.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = await repository.ClaimAsync(
                timeProvider
                    .GetUtcNow()
                    .UtcDateTime,
                policy.LeaseDuration,
                cancellationToken);

            if (claim is null)
                break;
            await ProcessAsync(
                claim,
                policy,
                cancellationToken);
            processed++;
        }

        return processed;
    }

    /// <summary>Processes one leased attempt, with bounded retries and a fenced acknowledgement.</summary>
    /// <param name="claim">The durable attempt claim.</param>
    /// <param name="policy">The validated policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the attempt is durably scheduled or completed.</returns>
    private async Task ProcessAsync(
        WishlistModerationEmailClaim claim,
        WishlistModerationEmailDeliveryPolicy policy,
        CancellationToken cancellationToken)
    {
        var now = timeProvider
            .GetUtcNow()
            .UtcDateTime;

        if (claim.AttemptCount > policy.MaximumAttempts)
        {
            await repository.CompleteAsync(
                claim,
                now,
                true,
                now,
                "AttemptsExhausted",
                cancellationToken);

            return;
        }

        var message = await repository.GetMessageAsync(
            claim,
            now,
            cancellationToken);

        if (message is null)
        {
            await repository.CompleteAsync(
                claim,
                now,
                true,
                now,
                "ResourceUnavailable",
                cancellationToken);

            return;
        }

        now = timeProvider
            .GetUtcNow()
            .UtcDateTime;
        var remainingLease = claim.LockedUntil - now;

        if (remainingLease <= TimeSpan.Zero)
            return;
        using var leaseTimeout = new CancellationTokenSource(
            remainingLease,
            timeProvider);
        using var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            leaseTimeout.Token);
        WishlistModerationEmailDeliveryException? failure = null;
        try
        {
            await sender.SendAsync(
                message,
                deliveryCancellation.Token);
        }
        catch (WishlistModerationEmailDeliveryException exception)
        {
            failure = exception;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {

            throw;
        }
        catch (OperationCanceledException)
        {
            failure = new WishlistModerationEmailDeliveryException(
                WishlistModerationEmailFailure.Transient,
                null);
        }
        catch (Exception)
        {
            failure = new WishlistModerationEmailDeliveryException(
                WishlistModerationEmailFailure.Unexpected,
                null);
        }

        now = timeProvider
            .GetUtcNow()
            .UtcDateTime;

        if (failure is not null)
        {
            var exhausted = claim.AttemptCount >= policy.MaximumAttempts;
            var retryAt = exhausted ? now : now.Add(policy.GetRetryDelay(
                    claim.AttemptCount,
                    failure.RetryAfter));
            await repository.CompleteAsync(
                claim,
                now,
                exhausted,
                retryAt,
                failure.Failure.ToString(),
                cancellationToken);
            WishlistModerationLogMessages.EmailFailed(
                logger,
                claim.EventId,
                failure.Failure);

            return;
        }

        await repository.CompleteAsync(
            claim,
            now,
            true,
            now,
            null,
            cancellationToken);
        WishlistModerationLogMessages.EmailSent(
            logger,
            claim.EventId);
    }
}
