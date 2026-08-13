using JennGllg.Fr.MonKado.Back.Application.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Worker;

internal sealed partial class UnconfirmedAccountCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<UnconfirmedAccountCleanupWorker> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan NormalInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextDelay = NormalInterval;
            try
            {
                int deletedCount = await DeleteExpiredAccounts(stoppingToken);
                if (deletedCount > 0)
                {
                    LogDeletedAccounts(deletedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                nextDelay = FailureRetryInterval;
                LogCleanupFailure(exception.GetType().Name);
            }

            await Task.Delay(nextDelay, timeProvider, stoppingToken);
        }
    }

    private async Task<int> DeleteExpiredAccounts(CancellationToken cancellationToken)
    {
        int totalDeleted = 0;
        int deletedInBatch;

        do
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IExpiredAccountCleanup cleanup = scope.ServiceProvider.GetRequiredService<IExpiredAccountCleanup>();
            deletedInBatch = await cleanup.DeleteExpiredUnconfirmedAccountsAsync(
                timeProvider.GetUtcNow(),
                BatchSize,
                cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch == BatchSize);

        return totalDeleted;
    }

    [LoggerMessage(2000, LogLevel.Information,
        "Deleted {DeletedAccountCount} expired unconfirmed accounts.")]
    private partial void LogDeletedAccounts(int deletedAccountCount);

    [LoggerMessage(2001, LogLevel.Warning,
        "Expired account cleanup failed and will be retried. Exception type: {ExceptionType}")]
    private partial void LogCleanupFailure(string exceptionType);
}
