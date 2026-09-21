using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Worker.Logging;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

/// <summary>Always purges erasure data and optionally delivers the pending notifications.</summary>
/// <param name="scopeFactory">The scoped maintenance and dispatcher factory.</param>
/// <param name="options">The validated processing intervals.</param>
/// <param name="emailOptions">The existing Gmail enablement configuration.</param>
/// <param name="timeProvider">The cycle clock.</param>
/// <param name="logger">The correlated technical logger.</param>
/// <param name="telemetry">The process-local operational measurements.</param>
public class AccountErasureWorker(
    IApplicationTelemetry telemetry,
    IServiceScopeFactory scopeFactory,
    IOptions<AccountErasureProcessingOptions> options,
    IOptions<AuthenticationEmailOptions> emailOptions,
    TimeProvider timeProvider,
    ILogger<AccountErasureWorker> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                var delay = await ProcessOnceAsync(stoppingToken);
                await Task.Delay(
                    delay,
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Keeps recipient expiry independent of whether a mail provider can be resolved.</summary>
    /// <param name="cancellationToken">The host cancellation token.</param>
    /// <returns>The next bounded cycle delay.</returns>
    private async Task<TimeSpan> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        telemetry.BeginCycle(WorkerOperation.AccountErasureProcessing);
        using var logScope = WorkerLogScope.Begin(
            logger,
            "AccountErasureProcessing");
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAccountErasureMaintenance>()
                .PurgeAsync(cancellationToken);

            if (emailOptions.Value.IsEnabled)
                await scope.ServiceProvider.GetRequiredService<IAccountErasureEmailDispatcher>()
                    .DispatchAsync(cancellationToken);

        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {

            throw;
        }
        catch (Exception)
        {
            AdministrativeAccountErasureLogMessages.ProcessingFailed(logger);

            telemetry.CompleteCycle(
                WorkerOperation.AccountErasureProcessing,
                options.Value.FailureInterval,
                false);

            return options.Value.FailureInterval;
        }

        telemetry.CompleteCycle(
            WorkerOperation.AccountErasureProcessing,
            options.Value.PollInterval,
            true);

        return options.Value.PollInterval;
    }
}
