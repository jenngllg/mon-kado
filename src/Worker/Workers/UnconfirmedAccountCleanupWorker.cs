using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Worker.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

internal sealed class UnconfirmedAccountCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<UnconfirmedAccountCleanupWorker> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan _normalInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan _failureRetryInterval = TimeSpan.FromMinutes(15);

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

    internal async Task<TimeSpan> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deletedCount = await DeleteExpiredAccountsAsync(cancellationToken);

            if (deletedCount > 0)
            {
                WorkerLogMessages.ExpiredAccountsDeleted(
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
            WorkerLogMessages.ExpiredAccountCleanupFailed(
                logger,
                exception.GetType().Name);

            return _failureRetryInterval;
        }
    }

    internal async Task<int> DeleteExpiredAccountsAsync(CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        int deletedInBatch;

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredAccountCleanup>();
            deletedInBatch = await cleanup.DeleteExpiredUnconfirmedAccountsAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                BatchSize,
                cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch == BatchSize);

        return totalDeleted;
    }

}
