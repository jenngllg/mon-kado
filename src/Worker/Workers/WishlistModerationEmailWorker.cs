using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Worker.Logging;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

/// <summary>Delivers durable moderation notifications and purges their processed outbox entries.</summary>
/// <param name="scopeFactory">The scoped dispatcher factory.</param>
/// <param name="options">The moderation delivery configuration.</param>
/// <param name="emailOptions">The existing configured Gmail enablement.</param>
/// <param name="timeProvider">The delay and UTC clock.</param>
/// <param name="logger">The technical structured logger.</param>
public class WishlistModerationEmailWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WishlistModerationEmailOptions> options,
    IOptions<AuthenticationEmailOptions> emailOptions,
    TimeProvider timeProvider,
    ILogger<WishlistModerationEmailWorker> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        if (!emailOptions.Value.IsEnabled)
            return;
        var configuration = options.Value;
        var policy = new WishlistModerationEmailDeliveryPolicy(
            configuration.BatchSize,
            configuration.LeaseDuration,
            configuration.MaximumAttempts,
            configuration.RetryDelays,
            configuration.MaximumRetryDelay,
            TimeSpan.FromDays(configuration.ProcessedRetentionDays));
        while (true)
        {
            try
            {
                var nextDelay = await DispatchOnceAsync(
                    policy,
                    stoppingToken);
                await Task.Delay(
                    nextDelay,
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Runs one correlated delivery and cleanup cycle without leaking failure details.</summary>
    /// <param name="policy">The validated policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next configured cycle delay.</returns>
    private async Task<TimeSpan> DispatchOnceAsync(
        WishlistModerationEmailDeliveryPolicy policy,
        CancellationToken cancellationToken)
    {
        using var logScope = WorkerLogScope.Begin(
            logger,
            "WishlistModerationEmailDelivery");
        try
        {
            TimeSpan completedResult;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<IWishlistModerationEmailDispatcher>();
                await dispatcher.DispatchAsync(
                    policy,
                    cancellationToken);
                completedResult = options.Value.PollInterval;
            }

            return completedResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {

            throw;
        }
        catch (Exception)
        {
            WishlistModerationLogMessages.EmailCycleFailed(logger);

            return options.Value.FailureInterval;
        }
    }
}
