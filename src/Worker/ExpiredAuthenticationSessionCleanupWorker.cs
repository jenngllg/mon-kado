using JennGllg.Fr.MonKado.Back.Application.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Worker;

internal sealed partial class ExpiredAuthenticationSessionCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExpiredAuthenticationSessionCleanupWorker> logger) : BackgroundService
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
                int deletedCount = await DeleteExpiredSessions(stoppingToken);
                if (deletedCount > 0)
                {
                    LogDeletedSessions(deletedCount);
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

    internal async Task<int> DeleteExpiredSessions(CancellationToken cancellationToken)
    {
        int totalDeleted = 0;
        int deletedInBatch;

        do
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IExpiredAuthenticationSessionCleanup cleanup =
                scope.ServiceProvider.GetRequiredService<IExpiredAuthenticationSessionCleanup>();
            deletedInBatch = await cleanup.DeleteExpiredSessionsAsync(
                timeProvider.GetUtcNow(),
                BatchSize,
                cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch == BatchSize);

        return totalDeleted;
    }

    [LoggerMessage(3000, LogLevel.Information,
        "Deleted {DeletedSessionCount} expired authentication sessions.")]
    private partial void LogDeletedSessions(int deletedSessionCount);

    [LoggerMessage(3001, LogLevel.Warning,
        "Authentication session cleanup failed and will be retried. Exception type: {ExceptionType}")]
    private partial void LogCleanupFailure(string exceptionType);
}
