using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
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
        // Act
        await worker.ExecuteTask!.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
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

        // Assert
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(
            _frontendOrigin,
            dispatcher.FrontendOrigin);
        Assert.Equal(
            20,
            dispatcher.BatchSize);
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            dispatcher.LeaseDuration);
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
        await worker.ExecuteTask!.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSuccessfulIteration_UsesNormalPollingDelay()
    {
        // Arrange
        var dispatcher = new RecordingDispatcher();
        await using var provider = CreateProvider(dispatcher);
        var worker = CreateWorker(
            provider,
            EnabledOptions());

        // Act
        var delay = await worker.DispatchOnceAsync(
            _frontendOrigin,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            delay);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFailedIteration_UsesFailureDelay()
    {
        // Arrange
        var dispatcher = new RecordingDispatcher(new InvalidOperationException("Database unavailable."));
        await using var provider = CreateProvider(dispatcher);
        var worker = CreateWorker(
            provider,
            EnabledOptions());

        // Act
        var delay = await worker.DispatchOnceAsync(
            _frontendOrigin,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            delay);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIteration_PreservesHostCancellation()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        source.Cancel();
        var dispatcher = new RecordingDispatcher(new OperationCanceledException(source.Token));
        // Act
        await using var provider = CreateProvider(dispatcher);
        var worker = CreateWorker(
            provider,
            EnabledOptions());

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.DispatchOnceAsync(
                _frontendOrigin,
                source.Token));
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
        TimeProvider? timeProvider = null)
    {

        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(options),
            timeProvider ?? TimeProvider.System,
            NullLogger<AuthenticationEmailDeliveryWorker>.Instance);
    }

}
