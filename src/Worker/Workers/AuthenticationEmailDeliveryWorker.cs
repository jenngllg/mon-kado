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
/// Delivers pending authentication emails in the background.
/// </summary>
/// <param name="telemetry">The process-local operational measurements.</param>
/// <param name="scopeFactory">The scoped business dependencies.</param>
/// <param name="timeProvider">The UTC and scheduling clock.</param>
/// <param name="options">The validated processing configuration.</param>
/// <param name="logger">The structured operational logger.</param>
public sealed class AuthenticationEmailDeliveryWorker(
    IApplicationTelemetry telemetry,
    IServiceScopeFactory scopeFactory,
    IOptions<AuthenticationEmailOptions> options,
    TimeProvider timeProvider,
    ILogger<AuthenticationEmailDeliveryWorker> logger) : BackgroundService
{
    private readonly AuthenticationEmailOptions _emailOptions = options.Value;
    private readonly AuthenticationEmailDeliveryPolicy _deliveryPolicy = new(
        options.Value.DeliveryBatchSize,
        options.Value.DeliveryLeaseDuration,
        options.Value.MaximumDeliveryAttempts,
        options.Value.FirstRetryDelay,
        options.Value.SecondRetryDelay,
        options.Value.ThirdRetryDelay,
        options.Value.FourthRetryDelay,
        options.Value.SubsequentRetryDelay,
        options.Value.SlowRetryDelay,
        options.Value.MaximumRetryDelay);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        if (!_emailOptions.IsEnabled)
        {
            telemetry.Disable(WorkerOperation.AuthenticationEmailDelivery);
            using var logScope = WorkerLogScope.Begin(
                logger,
                "AuthenticationEmailDeliveryDisabled");
            WorkerLogMessages.AuthenticationEmailDeliveryDisabled(logger);

            return;
        }

        ArgumentNullException.ThrowIfNull(_emailOptions.FrontendOrigin);
        var frontendOrigin = new Uri(
            _emailOptions.FrontendOrigin,
            UriKind.Absolute);
        while (true)
        {
            try
            {
                TimeSpan nextDelay;
                using (WorkerLogScope.Begin(
                    logger,
                    "AuthenticationEmailDelivery"))
                {
                    nextDelay = await DispatchOnceAsync(
                        frontendOrigin,
                        stoppingToken);
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

    private async Task<TimeSpan> DispatchOnceAsync(
        Uri frontendOrigin,
        CancellationToken cancellationToken)
    {
        telemetry.BeginCycle(WorkerOperation.AuthenticationEmailDelivery);
        TimeSpan nextDelay;
        var successful = true;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dispatcher =
                scope.ServiceProvider.GetRequiredService<IAuthenticationEmailDispatcher>();
            await dispatcher.DispatchPendingAsync(
                frontendOrigin,
                _deliveryPolicy,
                cancellationToken);

            nextDelay = _emailOptions.PollInterval;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {

            throw;
        }
        catch (Exception exception)
        {
            WorkerLogMessages.AuthenticationEmailDeliveryFailed(
                logger,
                exception.GetType().Name,
                exception);

            successful = false;
            nextDelay = _emailOptions.FailureRetryInterval;
        }

        telemetry.CompleteCycle(
            WorkerOperation.AuthenticationEmailDelivery,
            nextDelay,
            successful);

        return nextDelay;
    }

}
