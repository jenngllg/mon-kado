using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Worker.Logging;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

/// <summary>
/// Removes expired unconfirmed accounts in the background.
/// </summary>
public sealed class UnconfirmedAccountCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<AuthenticationCleanupOptions> options,
    ILogger<UnconfirmedAccountCleanupWorker> logger) : BackgroundService
{
    private readonly AuthenticationCleanupOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                TimeSpan nextDelay;
                using (WorkerLogScope.Begin(
                    logger,
                    "UnconfirmedAccountCleanup"))
                {
                    nextDelay = await CleanupOnceAsync(stoppingToken);
                }
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
            var deletedCount = await DeleteExpiredAccountsAsync(cancellationToken);

            if (deletedCount > 0)
            {
                WorkerLogMessages.ExpiredAccountsDeleted(
                    logger,
                    deletedCount);
            }

            return _options.Interval;
        }
        catch (OperationCanceledException)
        {

            throw;
        }
        catch (Exception exception)
        {
            WorkerLogMessages.ExpiredAccountCleanupFailed(
                logger,
                exception.GetType().Name,
                exception);

            return _options.FailureRetryInterval;
        }
    }

    private async Task<int> DeleteExpiredAccountsAsync(CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        int deletedInBatch;

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredAccountCleanup>();
            deletedInBatch = await cleanup.DeleteExpiredUnconfirmedAccountsAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                _options.BatchSize,
                cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch == _options.BatchSize);

        return totalDeleted;
    }

}
