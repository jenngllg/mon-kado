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
/// Removes expired member email change requests in the background.
/// </summary>
/// <param name="telemetry">The process-local operational measurements.</param>
/// <param name="scopeFactory">The scoped business dependencies.</param>
/// <param name="timeProvider">The UTC and scheduling clock.</param>
/// <param name="options">The validated processing configuration.</param>
/// <param name="logger">The structured operational logger.</param>
public sealed class ExpiredMemberEmailChangeRequestCleanupWorker(
    IApplicationTelemetry telemetry,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<AuthenticationCleanupOptions> options,
    ILogger<ExpiredMemberEmailChangeRequestCleanupWorker> logger) : BackgroundService
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
                    "ExpiredMemberEmailChangeRequestCleanup"))
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
        telemetry.BeginCycle(WorkerOperation.ExpiredMemberEmailChangeRequestCleanup);
        try
        {
            var deletedCount = await DeleteExpiredRequestsAsync(cancellationToken);

            if (deletedCount > 0)
            {
                WorkerLogMessages.ExpiredMemberEmailChangeRequestsDeleted(
                    logger,
                    deletedCount);
            }

            telemetry.CompleteCycle(
                WorkerOperation.ExpiredMemberEmailChangeRequestCleanup,
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
            WorkerLogMessages.MemberEmailChangeRequestCleanupFailed(
                logger,
                exception.GetType().Name,
                exception);

            telemetry.CompleteCycle(
                WorkerOperation.ExpiredMemberEmailChangeRequestCleanup,
                _options.FailureRetryInterval,
                false);

            return _options.FailureRetryInterval;
        }
    }

    private async Task<int> DeleteExpiredRequestsAsync(CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        int deletedInBatch;

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var cleanup = scope.ServiceProvider
                .GetRequiredService<IExpiredMemberEmailChangeRequestCleanup>();
            deletedInBatch = await cleanup.DeleteExpiredRequestsAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                _options.BatchSize,
                cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch == _options.BatchSize);

        return totalDeleted;
    }
}
