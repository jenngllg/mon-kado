using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Worker.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker;

internal sealed partial class AuthenticationEmailDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AuthenticationEmailOptions> options,
    TimeProvider timeProvider,
    ILogger<AuthenticationEmailDeliveryWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FailureInterval = TimeSpan.FromMinutes(1);
    private readonly AuthenticationEmailOptions emailOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!emailOptions.IsEnabled)
        {
            LogDeliveryDisabled();
            return;
        }

        Uri frontendOrigin = new(emailOptions.FrontendOrigin!, UriKind.Absolute);
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextDelay = PollInterval;
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IAuthenticationEmailDispatcher dispatcher =
                    scope.ServiceProvider.GetRequiredService<IAuthenticationEmailDispatcher>();
                await dispatcher.DispatchPendingAsync(
                    frontendOrigin,
                    BatchSize,
                    LeaseDuration,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                nextDelay = FailureInterval;
                LogDeliveryFailure(exception.GetType().Name);
            }

            await Task.Delay(nextDelay, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(2100, LogLevel.Information,
        "Authentication email delivery is disabled for this environment.")]
    private partial void LogDeliveryDisabled();

    [LoggerMessage(2101, LogLevel.Warning,
        "Authentication email delivery failed and will be retried. Exception type: {ExceptionType}")]
    private partial void LogDeliveryFailure(string exceptionType);
}
