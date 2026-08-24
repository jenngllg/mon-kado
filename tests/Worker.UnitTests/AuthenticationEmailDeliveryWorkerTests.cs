using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class AuthenticationEmailDeliveryWorkerTests
{
    private static readonly Uri _frontendOrigin = new("https://mon-kado.fr");

    [Fact]
    public async Task ExecuteAsync_WhenDisabledWorkerCompletesWithoutResolvingDispatcher_Completes()
    {
        // Arrange
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var worker = CreateWorker(
            provider,
            new AuthenticationEmailOptions { Provider = AuthenticationEmailOptions.DisabledProvider });

        await worker.StartAsync(TestContext.Current.CancellationToken);
        var executeTask = GetExecuteTask(worker);

        // Act
        await executeTask.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(executeTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabledWorkerDispatchesAnd_StopsCleanlyDuringPollingDelay()
    {
        // Arrange
        var dispatcher = new RecordingDispatcher();
        await using var provider = CreateProvider(dispatcher);
        var worker = CreateWorker(
            provider,
            EnabledOptions());

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await dispatcher.Called.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        // Act
        await worker.StopAsync(TestContext.Current.CancellationToken);
        var executeTask = GetExecuteTask(worker);

        // Assert
        Assert.True(executeTask.IsCompletedSuccessfully);
        Assert.Equal(
            _frontendOrigin,
            dispatcher.FrontendOrigin);
        Assert.NotNull(dispatcher.Policy);
        Assert.Equal(
            20,
            dispatcher.Policy.BatchSize);
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            dispatcher.Policy.LeaseDuration);
        Assert.Equal(
            10,
            dispatcher.Policy.MaximumAttempts);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDelayCompletes_StartsNextIteration()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var dispatcher = new RecordingDispatcher
        {
            OnCall = callCount =>
            {

                if (callCount == 2)
                    source.Cancel();
            }
        };
        await using var provider = CreateProvider(dispatcher);
        var worker = CreateWorker(
            provider,
            EnabledOptions(),
            new ImmediateTimeProvider());

        // Act
        await worker.StartAsync(source.Token);
        var executeTask = GetExecuteTask(worker);
        await executeTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(executeTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIterationFails_RetriesUntilHostStops()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var exception = new InvalidOperationException("Database unavailable.");
        var dispatcher = new RecordingDispatcher(exception)
        {
            OnCall = _ => source.Cancel()
        };
        await using var provider = CreateProvider(dispatcher);
        var logger = new RecordingLogger<AuthenticationEmailDeliveryWorker>();
        var worker = CreateWorker(
            provider,
            EnabledOptions(),
            logger: logger);

        // Act
        await worker.StartAsync(source.Token);
        var executeTask = GetExecuteTask(worker);
        await executeTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(executeTask.IsCompletedSuccessfully);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(
            LogLevel.Error,
            entry.LogLevel);
        Assert.Same(
            exception,
            entry.Exception);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDispatchIsCanceled_PreservesHostCancellation()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var dispatcher = new RecordingDispatcher(new OperationCanceledException())
        {
            OnCall = _ => source.Cancel()
        };
        await using var provider = CreateProvider(dispatcher);
        var worker = CreateWorker(
            provider,
            EnabledOptions());

        // Act
        await worker.StartAsync(source.Token);
        var executeTask = GetExecuteTask(worker);
        await executeTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(executeTask.IsCompletedSuccessfully);
    }

    private static AuthenticationEmailOptions EnabledOptions()
    {

        return new()
        {
            Provider = AuthenticationEmailOptions.GmailProvider,
            FrontendOrigin = _frontendOrigin.AbsoluteUri.TrimEnd('/')
        };
    }

    private static ServiceProvider CreateProvider(IAuthenticationEmailDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dispatcher);
        services.AddSingleton<IAuthenticationEmailDispatcher>(dispatcher);

        return services.BuildServiceProvider();
    }

    private static AuthenticationEmailDeliveryWorker CreateWorker(
        ServiceProvider provider,
        AuthenticationEmailOptions options,
        TimeProvider? timeProvider = null,
        ILogger<AuthenticationEmailDeliveryWorker>? logger = null)
    {

        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(options),
            timeProvider ?? TimeProvider.System,
            logger ?? NullLogger<AuthenticationEmailDeliveryWorker>.Instance);
    }

    private static Task GetExecuteTask(BackgroundService worker)
    {
        var executeTask = worker.ExecuteTask;
        Assert.NotNull(executeTask);

        return executeTask;
    }
}
