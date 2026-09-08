using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Delivers leased notifications before their absolute recipient deadline.</summary>
/// <param name="repository">The fenced outbox.</param>
/// <param name="protector">The operation-bound recipient protector.</param>
/// <param name="sender">The provider sender without transparent retries.</param>
/// <param name="options">The validated processing bounds.</param>
/// <param name="timeProvider">The UTC and timeout clock.</param>
/// <param name="logger">The technical outcome logger.</param>
public class AccountErasureEmailDispatcher(
    IAccountErasureEmailRepository repository,
    IAccountErasureRecipientProtector protector,
    IAccountErasureEmailSender sender,
    IOptions<AccountErasureProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<AccountErasureEmailDispatcher> logger) : IAccountErasureEmailDispatcher
{
    /// <inheritdoc/>
    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        while (processed < options.Value.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = await repository.ClaimAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                options.Value.LeaseDuration,
                options.Value.MaximumAttempts,
                cancellationToken);

            if (claim is null)
                break;
            await ProcessAsync(
                claim,
                cancellationToken);
            processed++;
        }

        return processed;
    }

    /// <summary>Sends once within the remaining lease and persists only a fenced outcome.</summary>
    /// <param name="claim">The durable claim.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task representing the bounded delivery attempt.</returns>
    private async Task ProcessAsync(
        AccountErasureEmailClaim claim,
        CancellationToken cancellationToken)
    {
        var deadline = claim.LockedUntil < claim.ExpiresAt ? claim.LockedUntil : claim.ExpiresAt;
        var remaining = deadline - timeProvider.GetUtcNow().UtcDateTime;

        if (remaining <= TimeSpan.Zero)
            return;
        using var timeout = new CancellationTokenSource(
            remaining,
            timeProvider);
        using var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        AccountErasureEmailDeliveryException? failure = null;
        try
        {
            var recipient = protector.Read(
                claim.OperationId,
                claim.ProtectedRecipient);

            if (recipient is null)
                throw new AccountErasureEmailDeliveryException(
                    AccountErasureEmailFailure.InvalidRecipient,
                    null);
            deliveryCancellation.Token.ThrowIfCancellationRequested();
            await sender.SendAsync(
                claim.OperationId,
                recipient,
                claim.CreatedAt,
                deliveryCancellation.Token);
        }
        catch (AccountErasureEmailDeliveryException exception)
        {
            failure = exception;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {

            throw;
        }
        catch (OperationCanceledException)
        {
            failure = new AccountErasureEmailDeliveryException(
                AccountErasureEmailFailure.Transient,
                null);
        }
        catch (Exception)
        {
            failure = new AccountErasureEmailDeliveryException(
                AccountErasureEmailFailure.Unexpected,
                null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (failure is null)
        {
            var acknowledged = await repository.CompleteAsync(
                claim,
                now,
                AccountErasureNotificationStatus.Accepted,
                now,
                cancellationToken);

            if (acknowledged)
                AdministrativeAccountErasureLogMessages.NotificationAccepted(
                    logger,
                    claim.OperationId);

            return;
        }

        var delay = GetRetryDelay(
            claim.AttemptCount,
            failure.RetryAfter);
        var retryAt = now.Add(delay);
        var terminal = claim.AttemptCount >= options.Value.MaximumAttempts || retryAt >= claim.ExpiresAt || failure.Failure is AccountErasureEmailFailure.InvalidRecipient or AccountErasureEmailFailure.Rejected;
        await repository.CompleteAsync(
            claim,
            now,
            terminal ? AccountErasureNotificationStatus.Failed : AccountErasureNotificationStatus.Pending,
            retryAt,
            cancellationToken);
        AdministrativeAccountErasureLogMessages.NotificationFailed(
            logger,
            claim.OperationId,
            failure.Failure);
    }

    /// <summary>Bounds provider-requested delays without shortening the configured backoff.</summary>
    /// <param name="attemptCount">The one-based durable attempt count.</param>
    /// <param name="retryAfter">The optional provider delay.</param>
    /// <returns>The positive bounded retry delay.</returns>
    private TimeSpan GetRetryDelay(
        int attemptCount,
        TimeSpan? retryAfter)
    {
        var delays = options.Value.RetryDelays;
        var index = Math.Clamp(
            attemptCount - 1,
            0,
            delays.Length - 1);
        var delay = delays[index];

        if (retryAfter is { } requested && requested > delay)
            delay = requested;

        return delay > options.Value.MaximumRetryDelay ? options.Value.MaximumRetryDelay : delay;
    }
}
