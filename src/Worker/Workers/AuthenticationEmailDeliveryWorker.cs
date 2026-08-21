using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Worker.Logging;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

internal sealed class AuthenticationEmailDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AuthenticationEmailOptions> options,
    TimeProvider timeProvider,
    ILogger<AuthenticationEmailDeliveryWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan _leaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _failureInterval = TimeSpan.FromMinutes(1);
    private readonly AuthenticationEmailOptions _emailOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        if (!_emailOptions.IsEnabled)
        {
            WorkerLogMessages.AuthenticationEmailDeliveryDisabled(logger);

            return;
        }

        var frontendOrigin = new Uri(
            _emailOptions.FrontendOrigin!,
            UriKind.Absolute);
        while (true)
        {
            try
            {
                var nextDelay = await DispatchOnceAsync(
                    frontendOrigin,
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

    internal async Task<TimeSpan> DispatchOnceAsync(
        Uri frontendOrigin,
        CancellationToken cancellationToken)
    {
        TimeSpan nextDelay;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dispatcher =
                scope.ServiceProvider.GetRequiredService<IAuthenticationEmailDispatcher>();
            await dispatcher.DispatchPendingAsync(
                frontendOrigin,
                BatchSize,
                _leaseDuration,
                cancellationToken);

            nextDelay = _pollInterval;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {

            throw;
        }
        catch (Exception exception)
        {
            WorkerLogMessages.AuthenticationEmailDeliveryFailed(
                logger,
                exception.GetType().Name);

            nextDelay = _failureInterval;
        }

        return nextDelay;
    }

}
