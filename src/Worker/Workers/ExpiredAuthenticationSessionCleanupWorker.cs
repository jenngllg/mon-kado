using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Worker.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

/// <summary>
/// Removes expired authentication sessions in the background.
/// </summary>
public sealed class ExpiredAuthenticationSessionCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExpiredAuthenticationSessionCleanupWorker> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan _normalInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan _failureRetryInterval = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                var nextDelay = await CleanupOnceAsync(stoppingToken);
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

    private async Task<TimeSpan> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deletedCount = await DeleteExpiredSessionsAsync(cancellationToken);

            if (deletedCount > 0)
            {
                WorkerLogMessages.ExpiredSessionsDeleted(
                    logger,
                    deletedCount);
            }

            return _normalInterval;
        }
        catch (OperationCanceledException)
        {

            throw;
        }
        catch (Exception exception)
        {
            WorkerLogMessages.ExpiredSessionCleanupFailed(
                logger,
                exception.GetType().Name,
                exception);

            return _failureRetryInterval;
        }
    }

    private async Task<int> DeleteExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        int deletedInBatch;

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cleanup =
                scope.ServiceProvider.GetRequiredService<IExpiredAuthenticationSessionCleanup>();
            deletedInBatch = await cleanup.DeleteExpiredSessionsAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                BatchSize,
                cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch == BatchSize);

        return totalDeleted;
    }

}
