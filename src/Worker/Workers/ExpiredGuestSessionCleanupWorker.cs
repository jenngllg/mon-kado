using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Worker.Logging;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

/// <summary>
/// Removes expired browser guest sessions in the background.
/// </summary>
/// <param name="telemetry">The process-local operational measurements.</param>
/// <param name="scopeFactory">The scoped business dependencies.</param>
/// <param name="timeProvider">The UTC and scheduling clock.</param>
/// <param name="options">The validated processing configuration.</param>
/// <param name="logger">The structured operational logger.</param>
public sealed class ExpiredGuestSessionCleanupWorker(
    IApplicationTelemetry telemetry,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<AuthenticationCleanupOptions> options,
    ILogger<ExpiredGuestSessionCleanupWorker> logger) : BackgroundService
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
                    "ExpiredGuestSessionCleanup"))
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
        telemetry.BeginCycle(WorkerOperation.ExpiredGuestSessionCleanup);
        try
        {
            var deletedCount = await DeleteExpiredSessionsAsync(cancellationToken);

            if (deletedCount > 0)
            {
                WorkerLogMessages.ExpiredGuestSessionsDeleted(
                    logger,
                    deletedCount);
            }

            telemetry.CompleteCycle(
                WorkerOperation.ExpiredGuestSessionCleanup,
                _options.Interval,
                true);

            return _options.Interval;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            WorkerLogMessages.ExpiredGuestSessionCleanupFailed(
                logger,
                exception.GetType().Name,
                exception);

            telemetry.CompleteCycle(
                WorkerOperation.ExpiredGuestSessionCleanup,
                _options.FailureRetryInterval,
                false);

            return _options.FailureRetryInterval;
        }
    }

    private async Task<int> DeleteExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        int deletedInBatch;

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredGuestSessionCleanup>();
            deletedInBatch = await cleanup.DeleteExpiredSessionsAsync(
                _options.BatchSize,
                cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch == _options.BatchSize);

        return totalDeleted;
    }
}
